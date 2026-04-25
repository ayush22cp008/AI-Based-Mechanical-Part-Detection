import cv2, imagehash, os
from PIL import Image
from config import *

def clean_keep():
    src = os.path.join(SELECTED_DIR_NAME, "KEEP")
    dst = CLEAN_DIR_NAME
    os.makedirs(dst, exist_ok=True)

    hashes = []

    for f in os.listdir(src):
        p = os.path.join(src, f)
        img = cv2.imread(p)
        if img is None: continue

        if cv2.Laplacian(cv2.cvtColor(img, cv2.COLOR_BGR2GRAY), cv2.CV_64F).var() < BLUR_THRESHOLD_CLEAN:
            continue

        h = imagehash.phash(Image.open(p))
        # Balanced threshold 14: Removes exact duplicates but keeps similar useful angles
        if any(abs(h - x) < 14 for x in hashes):
            continue

        hashes.append(h)
        cv2.imwrite(os.path.join(dst, f), img)

if __name__ == "__main__":
    clean_keep()
