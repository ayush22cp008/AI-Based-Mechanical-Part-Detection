import subprocess
import sys
from pathlib import Path
from config import *

# ===================== CONFIG =====================
URL_FILE = "youtube_urls.txt"
VIDEO_DIR = Path(VIDEO_DIR_NAME)

# yt-dlp format selector:
# - Prefer 720p for better ML training data
# - Fallback to best available if 720p not found
FORMAT = "best[height<=720][ext=mp4]/best[height<=720]/best"

# =================================================


def download_videos():
    if not Path(URL_FILE).exists():
        print(f"[ERROR] URL file not found: {URL_FILE}")
        return

    VIDEO_DIR.mkdir(parents=True, exist_ok=True)

    with open(URL_FILE, "r") as f:
        urls = [u.strip() for u in f if u.strip()]

    if not urls:
        print("[ERROR] No URLs found")
        return

    print(f"[INFO] Found {len(urls)} URLs")

    for idx, url in enumerate(urls, start=1):
        print(f"\n[{idx}/{len(urls)}] Downloading video")

        cmd = [
            sys.executable, "-m", "yt_dlp",
            "-f", FORMAT,
            "--no-part",                     # no .part files
            "--retries", "10",
            "--socket-timeout", "30", 
            "--force-overwrites",
            "-o", str(VIDEO_DIR / "%(title)s [%(id)s].%(ext)s"),
            url
        ]

        try:
            subprocess.run(cmd, check=True)
            print("✅ Download complete")
        except subprocess.CalledProcessError:
            print("❌ Download failed, skipping this URL")

    print("\n🎯 All download attempts finished")


if __name__ == "__main__":
    download_videos()
