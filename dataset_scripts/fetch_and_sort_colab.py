import os
import shutil
from pathlib import Path
import cv2
import numpy as np
from tqdm import tqdm

# --- COLAB LOCAL CONFIGURATION ---
# The dataset is expected to be unzipped in /content
BASE_DIR = "/content"

RAW_SOURCE = f"{BASE_DIR}/no_augmentation_3d_img.v2i.yolov8"
REAL_DEST = f"{BASE_DIR}/real_dataset"
RENDER_DEST = f"{BASE_DIR}/renders_dataset"

def is_render_background(image_path):
    img = cv2.imread(str(image_path))
    if img is None: return False
    h, w = img.shape[:2]
    if h < 2 or w < 2: return False
    edge_pixels = np.concatenate([img[0, :], img[-1, :], img[:, 0], img[:, -1]])
    channel_vars = [np.var(edge_pixels[:, i]) for i in range(3)]
    if max(channel_vars) > 12.0: return False 
    avg_color = np.mean(edge_pixels, axis=0)
    if max(avg_color) - min(avg_color) > 10.0: return False
    return True

def fetch_and_sort():
    src_base = Path(RAW_SOURCE)
    real_base = Path(REAL_DEST)
    render_base = Path(RENDER_DEST)

    print(f"Colab Fetching: Sorting raw data into Real and Render pools...")
    
    counts = {"real": 0, "render": 0}

    for split in ['train', 'valid', 'test']:
        img_dir = src_base / split / "images"
        if not img_dir.exists(): continue
        
        # Prepare destinations
        (real_base / split / "images").mkdir(parents=True, exist_ok=True)
        (real_base / split / "labels").mkdir(parents=True, exist_ok=True)
        (render_base / split / "images").mkdir(parents=True, exist_ok=True)
        (render_base / split / "labels").mkdir(parents=True, exist_ok=True)

        for img_p in tqdm(list(img_dir.glob("*.jpg")), desc=f"Processing {split}"):
            lab_p = src_base / split / "labels" / (img_p.stem + ".txt")
            if not lab_p.exists(): continue

            # Decide if Render or Real
            if is_render_background(img_p):
                dest_base = render_base / split
                type_key = "render"
            else:
                dest_base = real_base / split
                type_key = "real"

            shutil.copy2(img_p, dest_base / "images" / img_p.name)
            shutil.copy2(lab_p, dest_base / "labels" / lab_p.name)
            counts[type_key] += 1

    print("\n--- Raw Sorting Summary ---")
    print(f"Real Images Found: {counts['real']}")
    print(f"Render Images Found: {counts['render']}")
    print(f"Done! Use relocate_renders_colab.py next for manual cleaning.")

if __name__ == "__main__":
    fetch_and_sort()
