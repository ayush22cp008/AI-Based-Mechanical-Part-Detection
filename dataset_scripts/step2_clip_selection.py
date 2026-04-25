
# import os
# import csv
# import shutil
# import torch
# import clip
# from PIL import Image
# from tqdm import tqdm
# from config import *

# def image_selection_agent():
#     device = "cpu"  # change to "cuda" if available
#     model, preprocess = clip.load("ViT-B/32", device=device)

#     # --------------------------------------------------
#     # Encode text prompts
#     # --------------------------------------------------
#     def encode_text(prompts):
#         tokens = clip.tokenize(prompts).to(device)
#         with torch.no_grad():
#             emb = model.encode_text(tokens)
#         return emb / emb.norm(dim=-1, keepdim=True)

#     part_emb = encode_text(PART_PROMPTS)
#     obs_emb  = encode_text(OBSTRUCTION_PROMPTS)

#     # --------------------------------------------------
#     # Output folders
#     # --------------------------------------------------fvfdfdffddft
#     base_out = f"selected_images_{PART_NAME}"
#     keep_dir   = os.path.join(base_out, "KEEP")
#     review_dir = os.path.join(base_out, "REVIEW")
#     reject_dir = os.path.join(base_out, "REJECT")

#     os.makedirs(keep_dir, exist_ok=True)
#     os.makedirs(review_dir, exist_ok=True)
#     os.makedirs(reject_dir, exist_ok=True)

#     # --------------------------------------------------
#     # CSV log
#     # --------------------------------------------------
#     log_path = os.path.join(base_out, "selection_log.csv")
#     log = open(log_path, "w", newline="")
#     writer = csv.writer(log)
#     writer.writerow(["image_path", "diff_score", "decision"])

#     print("[INFO] Running CLIP image selection...")

#     # --------------------------------------------------
#     # Walk through frames
#     # --------------------------------------------------
#     for root, _, files in os.walk(FRAME_DIR_NAME):
#         for fname in tqdm(files):
#             if not fname.lower().endswith(".jpg"):
#                 continue

#             src_path = os.path.join(root, fname)

#             try:
#                 img = preprocess(
#                     Image.open(src_path).convert("RGB")
#                 ).unsqueeze(0).to(device)

#                 with torch.no_grad():
#                     img_emb = model.encode_image(img)

#                 img_emb = img_emb / img_emb.norm(dim=-1, keepdim=True)

#                 # CLIP score difference
#                 diff = (img_emb @ part_emb.T).mean() - (img_emb @ obs_emb.T).mean()
#                 diff_val = diff.item()

#                 # Decision
#                 if diff_val > KEEP_MARGIN:
#                     decision = "KEEP"
#                     dst_dir = keep_dir
#                 elif diff_val < REJECT_MARGIN:
#                     decision = "REJECT"
#                     dst_dir = reject_dir
#                 else:
#                     decision = "REVIEW"
#                     dst_dir = review_dir

#                 # Copy image
#                 dst_path = os.path.join(dst_dir, fname)
#                 shutil.copy2(src_path, dst_path)

#                 writer.writerow([src_path, round(diff_val, 4), decision])

#             except Exception as e:
#                 print("[ERROR]", src_path, e)

#     log.close()
#     print("[DONE] Selection completed.")
#     print(f"[INFO] Results saved in: {base_out}")

# if __name__ == "__main__":
#     image_selection_agent()
    

import os
import csv
import shutil
import sys
import io
import torch
import clip
from PIL import Image
from tqdm import tqdm
from config import *

# --------------------------------------------------
# Force UTF-8 output (Fix Windows charmap error)
# --------------------------------------------------
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="ignore")
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="ignore")


def image_selection_agent():

    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"[INFO] Using device: {device}")

    model, preprocess = clip.load("ViT-B/32", device=device)

    # --------------------------------------------------
    # Encode text prompts
    # --------------------------------------------------
    def encode_text(prompts):
        tokens = clip.tokenize(prompts).to(device)
        with torch.no_grad():
            emb = model.encode_text(tokens)
        return emb / emb.norm(dim=-1, keepdim=True)

    part_emb = encode_text(PART_PROMPTS)
    obs_emb = encode_text(OBSTRUCTION_PROMPTS)

    # --------------------------------------------------
    # Output folders
    # --------------------------------------------------
    base_out = SELECTED_DIR_NAME
    keep_dir = os.path.join(base_out, "KEEP")
    review_dir = os.path.join(base_out, "REVIEW")
    reject_dir = os.path.join(base_out, "REJECT")

    os.makedirs(keep_dir, exist_ok=True)
    os.makedirs(review_dir, exist_ok=True)
    os.makedirs(reject_dir, exist_ok=True)

    # --------------------------------------------------
    # CSV log (safe context manager)
    # --------------------------------------------------
    log_path = os.path.join(base_out, "selection_log.csv")

    print("[INFO] Running CLIP image selection...")

    with open(log_path, "w", newline="", encoding="utf-8") as log:
        writer = csv.writer(log)
        writer.writerow(["image_path", "diff_score", "decision"])

        # --------------------------------------------------
        # Walk through frames
        # --------------------------------------------------
        for root, _, files in os.walk(FRAME_DIR_NAME):

            jpg_files = [f for f in files if f.lower().endswith(".jpg")]

            for fname in tqdm(jpg_files, ascii=True):  # ascii=True fixes Windows unicode issue

                src_path = os.path.join(root, fname)

                try:
                    image = Image.open(src_path).convert("RGB")
                    img_tensor = preprocess(image).unsqueeze(0).to(device)

                    with torch.no_grad():
                        img_emb = model.encode_image(img_tensor)

                    img_emb = img_emb / img_emb.norm(dim=-1, keepdim=True)

                    diff = (img_emb @ part_emb.T).mean() - (
                        img_emb @ obs_emb.T
                    ).mean()

                    diff_val = float(diff.item())

                    # Decision logic
                    if diff_val > KEEP_MARGIN:
                        decision = "KEEP"
                        dst_dir = keep_dir
                    elif diff_val < REJECT_MARGIN:
                        decision = "REJECT"
                        dst_dir = reject_dir
                    else:
                        decision = "REVIEW"
                        dst_dir = review_dir

                    dst_path = os.path.join(dst_dir, fname)
                    shutil.copy2(src_path, dst_path)

                    writer.writerow([src_path, round(diff_val, 4), decision])

                except Exception as e:
                    # Safe error printing (no unicode crash)
                    print(f"[ERROR] Skipping file: {fname}")
                    print(f"Reason: {str(e)}")

    print("[DONE] Selection completed.")
    print(f"[INFO] Results saved in: {base_out}")


if __name__ == "__main__":
    image_selection_agent()
