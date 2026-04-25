from config import *
import subprocess
from pathlib import Path

# ================= CONFIG =================
PROJECT_ROOT = Path(__file__).resolve().parent

VIDEO_DIR = PROJECT_ROOT / VIDEO_DIR_NAME
FRAME_DIR = PROJECT_ROOT / FRAME_DIR_NAME

# FPS, VIDEO_EXTS can come from config or stay local if they are extraction-specific
VIDEO_EXTS = [".mp4", ".webm", ".mkv", ".avi", ".mov"]
# ==========================================


def extract_frames_from_videos():
    if not VIDEO_DIR.exists():
        print(f"[ERROR] Video folder not found: {VIDEO_DIR}")
        return

    FRAME_DIR.mkdir(parents=True, exist_ok=True)

    video_files = [
        v for v in VIDEO_DIR.iterdir()
        if v.suffix.lower() in VIDEO_EXTS
    ]

    if not video_files:
        print("[ERROR] No video files found in videos/")
        return

    print(f"[INFO] Found {len(video_files)} videos")

    for video_path in video_files:
        video_name = video_path.stem.replace(" ", "_")
        output_dir = FRAME_DIR / video_name
        output_dir.mkdir(parents=True, exist_ok=True)

        print(f"\n[INFO] Extracting from: {video_path.name}")

        ffmpeg_cmd = [
            "ffmpeg",
            "-y",
            "-i", str(video_path),
            "-vf", f"fps={FPS}",
            str(output_dir / "frame_%05d.jpg")
        ]

        subprocess.run(ffmpeg_cmd)

        print(f"[DONE] Frames → {output_dir}")


if __name__ == "__main__":
    extract_frames_from_videos()

