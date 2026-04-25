import cv2
import threading
import queue
import time
import collections
import numpy as np
from ultralytics import YOLO

# ============================================================
# CONFIGURATION
# ============================================================
MODEL_PATH        = "background_best.pt"
CONF_THRESHOLD    = 0.7        # detection confidence threshold
IOU_THRESHOLD     = 0.5        # NMS IoU threshold

# Camera  (capture / display resolution)
CAMERA_INDEX      = 1          # 0 = default webcam
CAM_WIDTH         = 640       # capture at high res → crisp display
CAM_HEIGHT        = 480
CAM_FPS           = 30

# Inference resolution  — frame is SHRUNK to this before YOLO, boxes scaled back up
# Smaller → faster inference.   Larger → better small-object accuracy.
# Keep the same aspect ratio as CAM_WIDTH × CAM_HEIGHT (16:9 here).
INFER_WIDTH       = 480        # 480×270 ≈ 3× fewer pixels than 1280×720
INFER_HEIGHT      = 270

# Threading queues (small = always-fresh frames, no lag build-up)
FRAME_QUEUE_SIZE  = 2
RESULT_QUEUE_SIZE = 2

# Tracking / smoothing
SMOOTH_WINDOW     = 5          # average box coords over last N frames per ID
GHOST_FRAMES      = 8          # keep showing a lost track for this many frames

# Tracker config: "bytetrack.yaml" is the most stable built-in YOLO tracker
TRACKER_CFG       = "bytetrack.yaml"

# ── Detection filters (false-positive suppression) ────────────────────────────
# Wires are thin & long → filter by extreme aspect ratio.
# Raise MAX_ASPECT_RATIO to allow more elongated boxes (e.g. chain links).
MAX_ASPECT_RATIO  = 4.0        # box w/h or h/w beyond this → rejected as wire
MIN_BOX_AREA      = 400        # pixels² in DISPLAY resolution; rejects tiny noise

# Classes to always ignore (add label strings from data.yaml if needed)
# e.g. EXCLUDED_CLASSES = {"chain", "spring"}  ← uncomment & fill if wires are
#      consistently misclassified as a specific class.
EXCLUDED_CLASSES: set[str] = set()   # empty = no class excluded

# ── CLAHE preprocessing (improves detection of dark/black objects) ─────────────
# Applied to the inference frame before YOLO sees it.
# clipLimit: higher = more contrast boost. tileGridSize: local region size.
CLAHE_CLIP        = 2.0
CLAHE_TILE        = (8, 8)
_clahe = cv2.createCLAHE(clipLimit=CLAHE_CLIP, tileGridSize=CLAHE_TILE)

# ============================================================
# PER-CLASS COLOR PALETTE  (BGR)
# ============================================================
# Auto-generated distinct colors for up to 20 classes
_PALETTE = [
    (0, 200, 255),   (0, 255, 100),   (255, 80,  0),   (200, 0, 255),
    (0, 120, 255),   (255, 220, 0),   (0, 255, 200),   (255, 0, 100),
    (100, 255, 0),   (255, 0, 200),   (0, 180, 180),   (180, 100, 0),
    (0, 80, 255),    (255, 150, 50),  (50, 255, 150),  (150, 50, 255),
    (255, 50, 150),  (50, 150, 255),  (200, 200, 0),   (0, 200, 100),
]

def class_color(cls_id: int):
    return _PALETTE[cls_id % len(_PALETTE)]

# ============================================================
# SHARED STATE
# ============================================================
frame_queue  = queue.Queue(maxsize=FRAME_QUEUE_SIZE)
result_queue = queue.Queue(maxsize=RESULT_QUEUE_SIZE)

stop_event   = threading.Event()
model_ready  = threading.Event()
camera_ready = threading.Event()

model = None
cap   = None

# ============================================================
# TRACKING STATE  (used only in inference thread → no locks needed)
# ============================================================
# track_id → deque of (x1,y1,x2,y2) for smoothing
smooth_boxes: dict[int, collections.deque] = {}
# track_id → (smoothed_box, label, conf, ghost_count_remaining)
ghost_tracks: dict[int, tuple] = {}

def smooth_box(track_id: int, box: tuple) -> tuple:
    """Push new box and return the running average (eliminates jitter)."""
    if track_id not in smooth_boxes:
        smooth_boxes[track_id] = collections.deque(maxlen=SMOOTH_WINDOW)
    smooth_boxes[track_id].append(box)
    arr = np.array(smooth_boxes[track_id])
    return tuple(arr.mean(axis=0).astype(int))

# ============================================================
# THREAD 1 – Load model
# ============================================================
def load_model():
    global model
    t0 = time.perf_counter()
    model = YOLO(MODEL_PATH)
    print(f"✅ Model loaded in {time.perf_counter() - t0:.2f}s")
    model_ready.set()

# ============================================================
# THREAD 2 – Open camera
# ============================================================
def open_camera():
    global cap
    t0 = time.perf_counter()
    cap = cv2.VideoCapture(CAMERA_INDEX, cv2.CAP_DSHOW)
    if not cap.isOpened():
        print("❌ Cannot access webcam")
        stop_event.set()
        return

    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)     # minimal OS buffer → fresher frames
    cap.set(cv2.CAP_PROP_FRAME_WIDTH,  CAM_WIDTH)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, CAM_HEIGHT)
    cap.set(cv2.CAP_PROP_FPS,          CAM_FPS)

    print(f"✅ Camera opened in {time.perf_counter() - t0:.2f}s  "
          f"({int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))}×"
          f"{int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))} "
          f"@ {int(cap.get(cv2.CAP_PROP_FPS))}fps)")
    camera_ready.set()

# ============================================================
# THREAD 3 – Capture frames continuously
# ============================================================
def capture_frames():
    camera_ready.wait()
    if stop_event.is_set():
        return

    while not stop_event.is_set():
        ret, frame = cap.read()
        if not ret:
            print("❌ Failed to grab frame")
            stop_event.set()
            break

        # Always keep the LATEST frame; discard stale ones
        if frame_queue.full():
            try:
                frame_queue.get_nowait()
            except queue.Empty:
                pass
        frame_queue.put(frame)

# ============================================================
# THREAD 4 – YOLO inference + ByteTrack tracking
# ============================================================
def run_inference():
    global ghost_tracks

    model_ready.wait()
    camera_ready.wait()

    while not stop_event.is_set():
        try:
            frame = frame_queue.get(timeout=0.5)
        except queue.Empty:
            continue

        # ----- Downscale frame for faster inference -----
        display_h, display_w = frame.shape[:2]          # original (display) size
        small_frame = cv2.resize(
            frame,
            (INFER_WIDTH, INFER_HEIGHT),
            interpolation=cv2.INTER_LINEAR               # fast + good quality
        )

        # Scale factors to map inference coords → display coords
        scale_x = display_w / INFER_WIDTH
        scale_y = display_h / INFER_HEIGHT

        # ----- CLAHE contrast enhancement (helps detect dark/black screws) -----
        # Convert to LAB, equalise only the L (lightness) channel, convert back.
        lab   = cv2.cvtColor(small_frame, cv2.COLOR_BGR2LAB)
        l, a, b = cv2.split(lab)
        l_eq  = _clahe.apply(l)
        small_frame = cv2.cvtColor(cv2.merge([l_eq, a, b]), cv2.COLOR_LAB2BGR)

        # ----- Run model on the enhanced / small frame -----
        results = model.track(
            small_frame,
            conf=CONF_THRESHOLD,
            iou=IOU_THRESHOLD,
            tracker=TRACKER_CFG,
            persist=True,
            verbose=False,
            imgsz=INFER_WIDTH,
            augment=False,
        )

        # ----- Build active track set this frame -----
        active_ids = set()
        annotated: list[dict] = []

        for r in results:
            if r.boxes is None:
                continue
            for box in r.boxes:
                # Skip detections without a track ID (shouldn't happen with persist=True)
                if box.id is None:
                    continue

                track_id = int(box.id[0])
                conf     = float(box.conf[0])
                cls      = int(box.cls[0])

                # Raw box is in inference (small) resolution → scale back to display res
                ix1, iy1, ix2, iy2 = box.xyxy[0].tolist()
                raw_box = (
                    int(ix1 * scale_x),
                    int(iy1 * scale_y),
                    int(ix2 * scale_x),
                    int(iy2 * scale_y),
                )

                label = model.names[cls]

                # ── Class exclusion filter ──────────────────────────────────────
                if label in EXCLUDED_CLASSES:
                    continue

                # ── Shape filter: reject wire-like elongated boxes ──────────────
                bx1, by1, bx2, by2 = raw_box
                bw = max(bx2 - bx1, 1)
                bh = max(by2 - by1, 1)
                area        = bw * bh
                aspect      = max(bw / bh, bh / bw)   # always ≥ 1

                if area   < MIN_BOX_AREA:              # skip tiny noise
                    continue
                if aspect > MAX_ASPECT_RATIO:          # skip thin wire-like shapes
                    continue

                # Temporal smoothing → stable, jitter-free boxes
                sbox = smooth_box(track_id, raw_box)

                active_ids.add(track_id)
                ghost_tracks[track_id] = (sbox, cls, conf, GHOST_FRAMES)
                annotated.append({
                    "track_id": track_id,
                    "box":  sbox,
                    "cls":  cls,
                    "conf": conf,
                    "ghost": False,
                })

        # ----- Propagate ghost tracks (objects briefly lost) -----
        expired = []
        for tid, (sbox, cls, conf, remaining) in ghost_tracks.items():
            if tid in active_ids:
                continue                         # already drawn above
            if remaining > 0:
                annotated.append({
                    "track_id": tid,
                    "box":  sbox,
                    "cls":  cls,
                    "conf": conf,
                    "ghost": True,              # drawn differently (dashed/faded)
                })
                ghost_tracks[tid] = (sbox, cls, conf, remaining - 1)
            else:
                expired.append(tid)

        for tid in expired:
            ghost_tracks.pop(tid, None)
            smooth_boxes.pop(tid, None)

        # Push to display queue
        if result_queue.full():
            try:
                result_queue.get_nowait()
            except queue.Empty:
                pass
        result_queue.put((frame, annotated))

# ============================================================
# HELPER – Draw detections & track info
# ============================================================
def draw_detections(frame: np.ndarray, annotated: list[dict]) -> np.ndarray:
    for det in annotated:
        track_id = det["track_id"]
        x1, y1, x2, y2 = det["box"]
        cls      = det["cls"]
        conf     = det["conf"]
        is_ghost = det["ghost"]
        label    = model.names[cls]
        color    = class_color(cls)

        if is_ghost:
            # Draw ghost as a dimmed / thinner box
            ghost_color = tuple(max(0, c // 3) for c in color)
            cv2.rectangle(frame, (x1, y1), (x2, y2), ghost_color, 1)
            continue

        # ---- Bounding box ----
        cv2.rectangle(frame, (x1, y1), (x2, y2), color, 2)

        # ---- Smart font scale ----
        box_height = y2 - y1
        font_scale = 0.9 if box_height < 80 else (0.75 if box_height < 200 else 0.6)
        thickness  = 2

        # ---- Two-line label: class + ID / confidence ----
        text_top    = f"{label}  ID:{track_id}"
        text_bottom = f"{int(conf * 100)}% conf"

        (w1, h1), _ = cv2.getTextSize(text_top,    cv2.FONT_HERSHEY_SIMPLEX, font_scale, thickness)
        (w2, h2), _ = cv2.getTextSize(text_bottom, cv2.FONT_HERSHEY_SIMPLEX, font_scale * 0.8, thickness - 1)

        label_w = max(w1, w2) + 6
        label_h = h1 + h2 + 16

        # Background pill
        cv2.rectangle(frame, (x1, y1 - label_h), (x1 + label_w, y1), color, -1)

        # Class text
        cv2.putText(frame, text_top,    (x1 + 3, y1 - h2 - 10),
                    cv2.FONT_HERSHEY_SIMPLEX, font_scale,       (0, 0, 0), thickness)
        # Confidence text (smaller)
        cv2.putText(frame, text_bottom, (x1 + 3, y1 - 4),
                    cv2.FONT_HERSHEY_SIMPLEX, font_scale * 0.8, (0, 0, 0), thickness - 1)

    return frame

# ============================================================
# HELPER – Overlay FPS counter
# ============================================================
_fps_times: collections.deque = collections.deque(maxlen=30)

def draw_fps(frame: np.ndarray) -> np.ndarray:
    now = time.perf_counter()
    _fps_times.append(now)
    if len(_fps_times) >= 2:
        fps = (len(_fps_times) - 1) / (_fps_times[-1] - _fps_times[0])
        cv2.putText(frame, f"FPS: {fps:.1f}", (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
    return frame

# ============================================================
# MAIN
# ============================================================
def main():
    t_model   = threading.Thread(target=load_model,     daemon=True)
    t_camera  = threading.Thread(target=open_camera,    daemon=True)
    t_capture = threading.Thread(target=capture_frames, daemon=True)
    t_infer   = threading.Thread(target=run_inference,  daemon=True)

    # Start loaders + pre-start threads (they block internally until ready)
    t_model.start()
    t_camera.start()
    t_capture.start()
    t_infer.start()

    print("⏳ Initialising model and camera in parallel…")

    model_ready.wait()
    camera_ready.wait()

    if stop_event.is_set():
        print("❌ Startup failed. Exiting.")
        return

    print("🚀 Webcam detection started. Press 'q' to quit.")

    display_frame = None

    while not stop_event.is_set():
        try:
            display_frame, annotated = result_queue.get(timeout=0.5)
            display_frame = draw_detections(display_frame, annotated)
        except queue.Empty:
            if display_frame is None:
                try:
                    display_frame = frame_queue.get_nowait()
                except queue.Empty:
                    pass

        if display_frame is not None:
            draw_fps(display_frame)
            cv2.imshow("YOLO Webcam Detection", display_frame)

        key = cv2.waitKey(1) & 0xFF
        if key == ord('q'):
            stop_event.set()
            break

    # ---- Cleanup ----
    stop_event.set()
    t_capture.join(timeout=2)
    t_infer.join(timeout=2)
    if cap is not None:
        cap.release()
    cv2.destroyAllWindows()
    print("✅ Webcam stopped.")


if __name__ == "__main__":
    main()