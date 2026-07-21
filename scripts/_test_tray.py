"""
Quick test to find ServicePilot tray icon and understand window setup.
"""
import subprocess
import time
import os
import sys
import win32gui
import win32con
import win32api
import pyautogui
from PIL import ImageGrab

APP_EXE = r"C:\git\家里\ServicePilot\ServicePilot\bin\Release\net8.0-windows\win-x64\ServicePilot.exe"

def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)

# Kill any existing
os.system('taskkill /f /im ServicePilot.exe 2>nul')
time.sleep(2)

# Launch
log("Launching app...")
proc = subprocess.Popen([APP_EXE], cwd=os.path.dirname(APP_EXE), shell=True)
time.sleep(5)

# Enumerate all windows to find ServicePilot-related ones
log("\n=== All visible windows ===")
def enum_cb(hwnd, _):
    if win32gui.IsWindowVisible(hwnd):
        cls = win32gui.GetClassName(hwnd)
        text = win32gui.GetWindowText(hwnd)
        parent = win32gui.GetParent(hwnd)
        if text or 'NotifyIcon' in cls or 'Tray' in cls:
            log(f"  0x{hwnd:08x} cls='{cls}' parent=0x{parent:08x} text='{text}'")
    return True
win32gui.EnumWindows(enum_cb, None)

# Also look for hidden windows related to ServicePilot
log("\n=== Hidden windows (no title, but important classes) ===")
def enum_hidden_cb(hwnd, _):
    if not win32gui.IsWindowVisible(hwnd):
        cls = win32gui.GetClassName(hwnd)
        text = win32gui.GetWindowText(hwnd)
        thread_id = win32gui.GetWindowThreadProcessId(hwnd)
        if 'Notify' in cls or 'Tray' in cls or 'Windows.UI' in cls or ('Window' in cls and not text):
            log(f"  0x{hwnd:08x} cls='{cls}' text='{text}' tid={thread_id}")
    return True
win32gui.EnumWindows(enum_hidden_cb, None)

# Find ServicePilot process
import psutil  # Might not be available
log("\n=== Process info ===")
os.system('tasklist /fi "imagename eq ServicePilot.exe" /v 2>nul')

# Check for popup window
popup = win32gui.FindWindow('#32768', None)
log(f"\nPopup menu window (#32768): {popup} (0x{popup:08x})" if popup else "\nNo popup menu")

# Capture the screen to see what we're working with
screen = ImageGrab.grab()
screen.save(r"C:\git\家里\ServicePilot\scripts\_screen_test.png")
log("Saved test screenshot")

# Get screen size
w, h = pyautogui.size()
log(f"\nScreen: {w}x{h}")

# Try right-clicking at bottom-right for tray menu
log("\n=== Trying to open tray menu ===")
time.sleep(1)

for try_y in [h-5, h-10, h-20, h-30]:
    for try_x in [w-30, w-50, w-80, w-120, w-160, w-200]:
        pyautogui.moveTo(try_x, try_y)
        time.sleep(0.05)
        pyautogui.rightClick(try_x, try_y)
        time.sleep(0.3)
        popup = win32gui.FindWindow('#32768', None)
        if popup and win32gui.IsWindowVisible(popup):
            log(f"  FOUND! Tray menu at ({try_x}, {try_y})")
            # Capture
            popup_rect = win32gui.GetWindowRect(popup)
            log(f"  Popup rect: {popup_rect}")
            # Take screenshot of the area
            img = ImageGrab.grab(bbox=(w-600, h-600, w, h))
            img.save(r"C:\git\家里\ServicePilot\scripts\_tray_test.png")
            log("  Saved tray test screenshot")
            pyautogui.press('esc')
            time.sleep(0.5)
            break
        pyautogui.press('esc')
        time.sleep(0.3)
    else:
        continue
    break

# Cleanup
os.system('taskkill /f /im ServicePilot.exe 2>nul')
