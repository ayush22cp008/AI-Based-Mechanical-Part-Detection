# AI-Based Mechanical Part Detection

A real-time computer vision system for identifying mechanical components using YOLOv8 Nano, deployed as a Unity mobile application.

---

## Overview

This project detects and classifies 25 types of mechanical parts (bolts, nuts, screws, bearings, gears, shafts, etc.) in real time using a mobile device camera. A custom dataset was built from scratch using synthetic 3D renders and real-world images. The trained YOLOv8 Nano model is integrated into an Android application built with Unity.

> **The Unity mobile app is the primary system.** The Python inference script is for local testing only.

---

## Features

- Real-time mechanical part detection via mobile camera
- **General Detection** — detects up to 5 components simultaneously
- **Specific Detection** — user selects a target component; system searches only for it
- Displays bounding boxes, class labels, and confidence scores
- Camera stability check to reduce motion blur and unnecessary inference
- Lightweight model optimized for mobile hardware

---

## Tech Stack

| Layer | Tools |
|---|---|
| Model | YOLOv8 Nano (Ultralytics) |
| Training | Python, PyTorch, OpenCV |
| Annotation | Roboflow, Segment Anything Model (SAM) |
| Dataset Optimization | DINOv2, K-Means Clustering |
| Mobile App | Unity (C#), AR Foundation |
| Deployment Target | Android |

---

## How It Works

1. **Dataset Collection** — Synthetic images rendered from 3D CAD models (Blender + TraceParts) and real-world images sourced from videos and online platforms.
2. **Annotation** — Segmentation-based labeling using SAM via Roboflow for pixel-level masks.
3. **Augmentation** — Background replacement technique applied to reduce background overfitting. Objects are isolated using segmentation masks and composited onto diverse backgrounds.
4. **Dataset Optimization** — DINOv2 feature embeddings + K-Means clustering used to remove redundant images and balance the dataset.
5. **Model Training** — YOLOv8 Nano trained on segmentation annotations (6,000 images across 25 classes).
6. **Mobile Deployment** — Trained model exported and integrated into Unity as a native plugin. AR Foundation manages the camera feed, preprocessing, and real-time rendering.

---

## Dataset Summary

| Split | Real | Synthetic | Augmented | Total |
|---|---|---|---|---|
| Train | 1,610 | 769 | 1,821 | 4,200 |
| Validation | 324 | 161 | 415 | 900 |
| Test | 342 | 157 | 401 | 900 |
| **Total** | **2,276** | **1,087** | **2,637** | **6,000** |

---

## Project Structure

```
AI-Based-Mechanical-Part-Detection/
│
├── dataset_scripts/        # Data collection, frame extraction, background replacement
├── training/               # YOLOv8 training scripts and config files
├── inference/              # Python webcam/image inference for local testing
├── unity_app/              # Unity project (Android mobile application)
└── outputs/                # Sample screenshots and detection results
```

---

## Results

- Model successfully detects mechanical parts across varied backgrounds and orientations
- Background replacement augmentation measurably improved real-world detection accuracy
- Stable real-time performance achieved on Android via camera stability logic and AR Foundation optimizations

**Sample Output**

> General Detection: detects nut (76%), screw (76%), screw (84%) simultaneously  
> Specific Detection: targets nut only — detected at 96% confidence

*(See `/outputs` for screenshots)*

---

## Challenges & Solutions

| Challenge | Solution |
|---|---|
| No public dataset available | Built custom dataset from 3D renders + real-world videos |
| Background overfitting | Background replacement augmentation using segmentation masks |
| Domain gap (synthetic vs real) | Combined synthetic, real, and augmented data during training |
| Mobile performance / latency | YOLOv8 Nano + AR Foundation async processing + stability-based inference |
| Annotation effort | Semi-automatic labeling with SAM via Roboflow |

---

## How to Run

### Mobile Application (Primary)

1. Open the `unity_app/` folder in Unity (tested with Unity 2022 LTS or later)
2. Ensure AR Foundation and Android Build Support packages are installed
3. Place the exported `.onnx` or compatible model file in the appropriate plugin folder
4. Build and deploy to an Android device (API level 24+)
5. Launch the app → select **General Detection** or **Specific Detection**

### Local Inference / Testing (Python)

```bash
# Install dependencies
pip install ultralytics opencv-python

# Run webcam inference
python inference/detect.py --source 0 --weights training/weights/best.pt

# Run on an image
python inference/detect.py --source path/to/image.jpg --weights training/weights/best.pt
```

### Model Training

```bash
pip install ultralytics

yolo task=segment mode=train \
  model=yolov8n-seg.pt \
  data=training/data.yaml \
  epochs=40 \
  imgsz=640
```

---

## Future Improvements

- Expand dataset to cover more mechanical part classes and edge cases
- Improve performance under low-light and high-occlusion conditions
- Explore model quantization (INT8) for further mobile optimization
- Add cloud inference option for higher-accuracy detection
- Integrate user feedback loop to continuously improve detection

---

## Author

**Ayush Halpati**  
B.Tech Computer Engineering — BVM Engineering College, Vallabh Vidyanagar  
Internship at Invisible Fiction, V.V. Nagar, Anand, Gujarat  

---

## License

This project is licensed under the [MIT License](LICENSE).
