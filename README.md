# AI-Based Mechanical Part Detection (YOLOv8 + Unity Mobile App)

A real-time computer vision system for identifying and classifying mechanical components, deployed as a native Android application built with Unity.

[![Model](https://img.shields.io/badge/Model-YOLOv8%20Nano-blue)](https://github.com/ultralytics/ultralytics)
[![Platform](https://img.shields.io/badge/Platform-Android-green)](https://developer.android.com/)
[![Framework](https://img.shields.io/badge/Framework-Unity%202022%20LTS-black)](https://unity.com/)
[![License](https://img.shields.io/badge/License-MIT-yellow)](LICENSE)

---

## Overview

This project detects and classifies **25 types of mechanical parts** — bolts, nuts, screws, bearings, gears, shafts, and more — in real time using a mobile device camera. A custom dataset was constructed from scratch using synthetic 3D renders, real-world images, and background-augmented composites. The trained YOLOv8 Nano model is deployed as a native plugin within an Android application built in Unity.

> **Primary System:** Unity mobile application (Android).  
> The Python inference script is included for local testing and development purposes only.

---

## Demo Video

A full walkthrough of the Android application — including both General and Specific Detection modes — is available on YouTube:

**[▶ Watch the Demo on YouTube](https://www.youtube.com/shorts/NPIHQJ7QCFs)**

The demo showcases real-time bounding box rendering, confidence score display, and the target-specific detection workflow on a live camera feed.

---

## Features

- **Real-time detection** via mobile device camera using a lightweight on-device model
- **General Detection** — simultaneously detects up to 5 mechanical components per frame
- **Specific Detection** — user selects a target class; the system searches exclusively for it
- Bounding box overlay with class labels and confidence scores
- Camera stability check to suppress inference during motion blur, reducing false detections
- Optimized for low-latency performance on Android mobile hardware

---

## Tech Stack

| Layer | Tools |
|---|---|
| Detection Model | YOLOv8 Nano (Ultralytics) |
| Training Framework | Python, PyTorch, OpenCV |
| Annotation | Roboflow, Segment Anything Model (SAM) |
| Dataset Optimization | DINOv2 embeddings, K-Means Clustering |
| Mobile Application | Unity (C#), AR Foundation |
| Deployment Target | Android (API level 24+) |

---

## How It Works

1. **Dataset Collection** — Synthetic images rendered from 3D CAD models (Blender + TraceParts) combined with real-world images sourced from video frames and public platforms.
2. **Annotation** — Pixel-level segmentation masks generated semi-automatically using SAM via Roboflow, significantly reducing manual labeling effort across 25 classes.
3. **Background Augmentation** — Objects are isolated via segmentation masks and composited onto diverse backgrounds to prevent background overfitting — a critical step for real-world generalization.
4. **Dataset Optimization** — DINOv2 feature embeddings combined with K-Means clustering to remove near-duplicate images and produce a balanced, representative dataset.
5. **Model Training** — YOLOv8 Nano trained on segmentation annotations across 6,000 images and 25 classes.
6. **Mobile Deployment** — Model exported and integrated into Unity as a native inference plugin. AR Foundation manages the camera pipeline, frame preprocessing, and real-time bounding box rendering.

---

## Dataset Summary

| Split | Real | Synthetic | Augmented | Total |
|---|---|---|---|---|
| Train | 1,610 | 769 | 1,821 | 4,200 |
| Validation | 324 | 161 | 415 | 900 |
| Test | 342 | 157 | 401 | 900 |
| **Total** | **2,276** | **1,087** | **2,637** | **6,000** |

---

## Results

The model successfully detects mechanical parts across varied backgrounds, lighting conditions, and orientations. Background replacement augmentation produced measurable improvements in real-world detection accuracy by closing the domain gap between synthetic training data and real-world inference conditions.

**Sample Detection Performance**

| Mode | Example Output |
|---|---|
| General Detection | Nut (76%), Screw (76%), Screw (84%) detected simultaneously |
| Specific Detection | Nut targeted — detected at **96% confidence** |

---

## Challenges & Solutions

| Challenge | Solution |
|---|---|
| No public dataset for mechanical parts | Built custom dataset from 3D renders and real-world video frames |
| Background overfitting from synthetic data | Background replacement augmentation using SAM segmentation masks |
| Domain gap between synthetic and real images | Combined synthetic, real, and augmented splits during training |
| Mobile inference latency constraints | YOLOv8 Nano + AR Foundation async pipeline + stability-based inference gating |
| Manual annotation effort at scale | Semi-automatic labeling with SAM via Roboflow |

---

## Project Structure

```
AI-Based-Mechanical-Part-Detection/
│
├── dataset_scripts/        # Data collection, frame extraction, background replacement
├── training/               # YOLOv8 training scripts and config files
├── inference/              # Python webcam/image inference (local testing only)
├── unity_app/              # Unity project — primary Android mobile application
```

---

## How to Run

### Mobile Application (Primary)

1. Open the `unity_app/` folder in Unity (2022 LTS or later)
2. Ensure **AR Foundation** and **Android Build Support** packages are installed
3. Place the exported `.onnx` (or compatible) model file in the appropriate plugin directory
4. Build and deploy to an Android device (API level 24+)
5. Launch the app → select **General Detection** or **Specific Detection**

### Local Inference / Testing (Python)

```bash
# Install dependencies
pip install ultralytics opencv-python

# Run webcam inference
python inference/detect.py --source 0 --weights training/weights/best.pt

# Run on a single image
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

## Author

**Ayush Halpati**  
B.Tech Computer Engineering — BVM Engineering College, Vallabh Vidyanagar  
Internship at Invisible Fiction, V.V. Nagar, Anand, Gujarat

---

## License

This project is licensed under the [MIT License](LICENSE).
