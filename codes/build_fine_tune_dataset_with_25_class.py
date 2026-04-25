import os
import torch
import cv2
import numpy as np
import shutil
from pathlib import Path
from tqdm import tqdm
from sklearn.cluster import KMeans
from torchvision import transforms
from PIL import Image
import yaml
import random
from collections import defaultdict

# --- CONFIGURATION MATCHING CHATGPT EXPERT ADVICE ---
# We are ignoring the old separated datasets and strictly using the new v1i goldmine.
BASE_DIR = r"/content" # Set to content for Colab, or change if running locally
INPUT_DIR = r"c:\Users\ayush\Desktop\final_25_class_dataset\all_class_real_images_final_25_classes.v1i.yolov8"
OUTPUT_DIR = r"c:\Users\ayush\Desktop\final_25_class_dataset\phase3_chatgpt_balanced_v4"

CLASS_NAMES = [
    'bearing', 'bearing block', 'bolt', 'bushing', 'chain', 'clamp', 'clutch', 
    'collar', 'coupling', 'gear', 'gearbox', 'hydraulic cylinder', 'impeller', 
    'knob', 'lever', 'motor pump', 'nut', 'pulley', 'screw', 'seal', 'shaft', 
    'snap ring', 'spring', 'valve', 'washer'
]

# CHATGPT RULES
MAX_TOTAL_PER_CLASS = 240
TRAIN_RATIO = 0.70
VALID_RATIO = 0.15

# --- DINOv2 INITIALIZATION ---
print(f"Loading dinov2_vits14...")
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
model = torch.hub.load('facebookresearch/dinov2', "dinov2_vits14")
model.to(device)
model.eval()

preprocess = transforms.Compose([
    transforms.Resize(224),
    transforms.CenterCrop(224),
    transforms.ToTensor(),
    transforms.Normalize([0.485,0.456,0.406],[0.229,0.224,0.225]),
])

def get_embedding(img_crop):
    img_pil = Image.fromarray(cv2.cvtColor(img_crop, cv2.COLOR_BGR2RGB))
    input_tensor = preprocess(img_pil).unsqueeze(0).to(device)
    with torch.no_grad():
        embedding = model(input_tensor)
    return embedding.cpu().numpy().flatten()

def sample_diverse_renders(render_images, num_needed):
    if len(render_images) <= num_needed:
        return render_images
    
    # We use DINO to cluster the renders and pick the most diverse ones
    print(f"Clustering {len(render_images)} renders down to {num_needed}...")
    embeddings = []
    valid_paths = []
    
    for img_p, lab_p in render_images:
        img = cv2.imread(str(img_p))
        if img is not None:
            emb = get_embedding(img)
            embeddings.append(emb)
            valid_paths.append((img_p, lab_p))
            
    if not embeddings: return []
    
    X = np.array(embeddings)
    kmeans = KMeans(n_clusters=num_needed, random_state=42, n_init='auto')
    kmeans.fit(X)
    
    # Find closest image to each cluster center
    selected = []
    for center_idx in range(num_needed):
        center = kmeans.cluster_centers_[center_idx]
        distances = np.linalg.norm(X - center, axis=1)
        closest_idx = np.argmin(distances)
        selected.append(valid_paths[closest_idx])
        # Inf out distance to not pick same image twice
        X[closest_idx] = np.inf 
        
    return selected

def main():
    print("Parsing Input Dataset...")
    
    # Separate paths
    classwise_real = defaultdict(list)
    classwise_render = defaultdict(list)
    
    for split in ["train", "valid", "test"]:
        label_dir = Path(INPUT_DIR) / split / "labels"
        image_dir = Path(INPUT_DIR) / split / "images"
        if not label_dir.exists(): continue
            
        for lab_p in label_dir.glob("*.txt"):
            try:
                with open(lab_p, 'r') as f:
                    lines = f.readlines()
                if not lines: continue
                # Primary class
                cid = int(lines[0].split()[0])
                
                # Check for corresponding image
                img_p = None
                for ext in [".jpg", ".jpeg", ".png", ".JPG"]:
                    p = image_dir / (lab_p.stem + ext)
                    if p.exists():
                        img_p = p
                        break
                if not img_p: continue
                    
                if "render" in img_p.name.lower():
                    classwise_render[cid].append((img_p, lab_p))
                else:
                    classwise_real[cid].append((img_p, lab_p))
            except:
                pass
                
    final_dataset = []
    
    for cid, name in enumerate(CLASS_NAMES):
        real_images = classwise_real[cid]
        render_images = classwise_render[cid]
        
        # Rule 1: Take ALL real images
        num_real = len(real_images)
        selected_real = real_images
        for p in selected_real:
            final_dataset.append((p[0], p[1], "real"))
            
        # Rule 2: Take Render images up to MAX_TOTAL_PER_CLASS (which is 240)
        renders_needed = MAX_TOTAL_PER_CLASS - num_real
        
        if renders_needed > 0:
            if len(render_images) > renders_needed:
                # Need to trim duplicate/extra renders using DINO
                selected_render = sample_diverse_renders(render_images, renders_needed)
            else:
                selected_render = render_images
                
            for p in selected_render:
                final_dataset.append((p[0], p[1], "render"))
                
        print(f"Class {cid} ({name:15}): Final Real = {len(selected_real):<4} | Final Render = {len(selected_render):<4} | Total = {len(selected_real)+len(selected_render)}")
        
    # Per-Class Splitting
    unique_selection = list({x[0]: x for x in final_dataset}.values())
    classwise_selection = defaultdict(list)
    
    for item in unique_selection:
        lab_p = item[1]
        with open(lab_p, 'r') as f:
            cid = int(f.readline().split()[0])
        classwise_selection[cid].append(item)
        
    train_set, val_set, test_set = [], [], []
    for cid, items in classwise_selection.items():
        random.shuffle(items)
        n = len(items)
        n_train = int(n * TRAIN_RATIO)
        n_valid = int(n * VALID_RATIO)
        train_set.extend(items[:n_train])
        val_set.extend(items[n_train:n_train+n_valid])
        test_set.extend(items[n_train+n_valid:])

    print("\nSplit Summary:")
    print(f"Train: {len(train_set)} | Valid: {len(val_set)} | Test : {len(test_set)}")

    def copy_files(dataset, split_name):
        img_out = Path(OUTPUT_DIR) / split_name / "images"
        lab_out = Path(OUTPUT_DIR) / split_name / "labels"
        img_out.mkdir(parents=True, exist_ok=True)
        lab_out.mkdir(parents=True, exist_ok=True)

        for img_p, lab_p, stype in tqdm(dataset, desc=f"Copying {split_name}"):
            shutil.copy2(img_p, img_out / img_p.name)
            shutil.copy2(lab_p, lab_out / lab_p.name)

    copy_files(train_set, "train")
    copy_files(val_set, "valid")
    copy_files(test_set, "test")

    # Save YAML
    data_yaml = {
        'train': 'train/images',
        'val': 'valid/images',
        'test': 'test/images',
        'nc': 25,
        'names': CLASS_NAMES
    }
    with open(Path(OUTPUT_DIR) / "data.yaml", 'w') as f:
        yaml.dump(data_yaml, f)

    print(f"\nFinal ChatGPT-Balanced Dataset ready at: {OUTPUT_DIR}")

if __name__ == "__main__":
    main()
