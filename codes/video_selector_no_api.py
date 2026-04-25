import yt_dlp
import time
import os

from config import PART_NAME

# ================= CONFIG ================= #

TARGET_VIDEO_COUNT = 15

MIN_DURATION_SECONDS = 15
MAX_DURATION_SECONDS = 600

# 🎯 LEVER - HIGH SUCCESS REALISM (RELAXED SEARCH + STRONG FILTERS)
SEARCH_QUERIES = [
    "industrial machinery control lever real footage",
    "mechanical control lever handle factory machines",
    "manual control lever machine room real camera",
    "industrial equipment lever handle manifold walkthrough",
    "machine shop lever assembly real footage",
    "heavy duty manual control lever showcase machine",
    "various mechanical levers on industrial machines",
    "factory tour showing machine control levers",
    "industrial equipment lever variety walkaround",
    "hydraulic control lever operation real camera"
]

# ❌ EXTREME BLOCKING OF ANIMATION / CGI / EDUCATIONAL / REPAIR CONTENT
FORBIDDEN_KEYWORDS = [
    # Animation / 3D / Simulation / AI / Synthetic
    "animation", "animated", "3d", "3-d", "three dimensional",
    "render", "simulation", "cgi", "graphic", "virtual",
    "cad", "solidworks", "blender", "fusion 360", "autocad",
    "3d print", "3d printing", "filament", "ansys", "abaqus",
    "toolbox", "simulink", "matlab", "creo", "solid works",
    "workbench analysis", "sketchup", "cinema 4d", "maya", 
    "3ds max", "keyshot", "digital illustration", "exploded view",
    "cross section", "motion graphic", "educational animation",
    "synthetic", "generated", "ai generated", "simulated",
    "ue5", "unreal engine", "unity", "gameplay",

    # Repair / Internals / Educational
    "repair", "overhaul", "fixing", "restoration", "dismantling",
    "inside", "how it works", "internal", "cutaway", "troubleshooting",
    "theory", "ppt", "explained", "calculation", "how it's made",
    "education", "tutorial", "lesson", "simplified", "diagram", "schematic",
    "blueprint", "illustration", "lever repair", "wrench", "rebuilding",

    # Non-Industrial / Consumer / Hobby
    "aquarium", "bicycle", "pool pump", "toy", "lego", "diy cardboard",
    "mini pump", "small dc pump", "775 motor pump"
]

# ✅ Filter for specific lever types (ANY of these in title)
REQUIRED_KEYWORDS_IN_TITLE = [
    "lever", "handle", "control lever", "manual lever", "mechanical lever", "hydraulic lever"
]

REQUIRED_KEYWORDS_IN_CONTENT = [
    "real", "camera", "footage", "handheld", "shaky", "vlog", "factory", "mechanical", "industry", "machine", "shop"
]

# ================= LOGIC ================= #

video_urls = []
seen_video_ids = set()

# Load existing URLs to avoid duplicates
existing_urls = set()
if os.path.exists("youtube_urls.txt"):
    with open("youtube_urls.txt", "r") as f:
        existing_urls = {line.strip() for line in f if line.strip()}

def is_forbidden(text: str) -> bool:
    text = text.lower()
    return any(word in text for word in FORBIDDEN_KEYWORDS)

def title_contains_required(title: str) -> bool:
    title = title.lower()
    return any(word in title for word in REQUIRED_KEYWORDS_IN_TITLE)

def content_contains_required(text: str) -> bool:
    text = text.lower()
    return any(word in text for word in REQUIRED_KEYWORDS_IN_CONTENT)

def get_video_info_safe(entry):
    # yt-dlp usually returns duration in seconds (int/float)
    duration = entry.get('duration')
    if duration is None:
        return 0
    try:
        return float(duration)
    except (ValueError, TypeError):
        return 0

print(f"Searching for {TARGET_VIDEO_COUNT} videos...\n")

ydl_opts = {
    'quiet': True,
    'skip_download': True,
    'ignoreerrors': True,
    'extract_flat': True, # Use flat extraction for speed and to avoid JS issues
    'nocheckcertificate': True,
}

with yt_dlp.YoutubeDL(ydl_opts) as ydl:
    
    for query in SEARCH_QUERIES:
        if len(video_urls) >= TARGET_VIDEO_COUNT:
            break
            
        print(f"Checking query: {query}")
        
        # Search for up to 30 items to have buffer for filtering
        try:
            search_query = f"ytsearch30:{query}"
            info = ydl.extract_info(search_query, download=False)
            
            if 'entries' not in info:
                continue
                
            for item in info['entries']:
                if item is None:
                    continue
                    
                if len(video_urls) >= TARGET_VIDEO_COUNT:
                    break
                    
                video_id = item.get('id')
                if not video_id or video_id in seen_video_ids:
                    continue
                
                title = item.get('title', '')
                description = item.get('description', '') or ""
                combined_text = f"{title} {description}"
                
                # ❌ Skip bad content (animation etc)
                if is_forbidden(combined_text):
                    continue
                
                # ✅ Title MUST contain required keywords (e.g. gearbox)
                if not title_contains_required(title):
                    continue
                
                # ✅ Must contain at least one real-world indicator
                if not content_contains_required(combined_text):
                    continue
                
                # ⏱ Duration filter
                duration = get_video_info_safe(item)
                
                if not duration:
                    continue
                    
                if duration < MIN_DURATION_SECONDS or duration > MAX_DURATION_SECONDS:
                    continue
                    
                seen_video_ids.add(video_id)
                video_url = item.get('webpage_url') or f"https://www.youtube.com/watch?v={video_id}"
                
                # Check if already in file
                if video_url in existing_urls:
                    print(f"  [-] Already in file: {title}")
                    continue

                video_urls.append(video_url)
                print(f"  [+] NEW Video Found: {title}")
                print(f"      URL: {video_url}")
                
        except Exception as e:
            print(f"  [!] Error with query '{query}': {e}")
            continue

# ================= OUTPUT ================= #

print(f"\n✅ {PART_NAME.upper()} VIDEOS:\n")

for idx, url in enumerate(video_urls, 1):
    print(f"{idx}. {url}")

with open("youtube_urls.txt", "a") as f:
    for url in video_urls:
        f.write(f"{url}\n") 

print(f"\nTotal New Collected and Saved: {len(video_urls)} videos")
print("Saved to youtube_urls.txt")

# Save to file to match expected workflow if needed, 
# or just print as the original script did.
# The original script printed to stdout. 
# But let's saving to youtube_urls.txt might be helpful as step0 reads it.
# However, the user didn't ask to overwrite it implicitly, so I'll stick to printing.
