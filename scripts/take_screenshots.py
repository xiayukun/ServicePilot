"""
ServicePilot v3.0.0 FluentWindow Screenshot Automation Script
Takes all required screenshots in Chinese and English.
"""
import subprocess
import time
import os
import sys
import json
import shutil
import win32gui
import win32con
import win32api
import pyautogui
from PIL import ImageGrab

# ── Config ──────────────────────────────────────────────────────────────────
PROJECT_DIR = r"C:\git\家里\ServicePilot"
APP_EXE = os.path.join(PROJECT_DIR, r"ServicePilot\bin\Release\net8.0-windows\win-x64\ServicePilot.exe")
SCREENSHOT_DIR = os.path.join(PROJECT_DIR, r"Assets\screenshots")
APP_DATA_CONFIG_DIR = os.path.join(os.environ.get('APPDATA', r'C:\Users\xiayukun\AppData\Roaming'), 'ServicePilot')
CONFIG_PATH = os.path.join(APP_DATA_CONFIG_DIR, 'config.v2.json')
CONFIG_BAK_PATH = CONFIG_PATH + '.bak-auto'

OUTPUT_DIR = SCREENSHOT_DIR  # Save directly
os.makedirs(OUTPUT_DIR, exist_ok=True)

pyautogui.FAILSAFE = False
pyautogui.PAUSE = 0.3

# ── Helpers ─────────────────────────────────────────────────────────────────

def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)

def kill_all_servicepilot():
    """Kill any running ServicePilot processes"""
    os.system('taskkill /f /im ServicePilot.exe 2>nul')
    os.system('taskkill /f /im ServicePilot 2>nul')
    time.sleep(2)

def wait_for_process(timeout=15):
    """Wait for ServicePilot.exe to appear in task list"""
    for i in range(timeout):
        r = os.system('tasklist /fi "imagename eq ServicePilot.exe" 2>nul | findstr ServicePilot >nul')
        if r == 0:
            return True
        time.sleep(1)
    return False

def find_window(title_contains, timeout=15):
    """Find a visible window by partial title match. Retry with timeout."""
    for _ in range(timeout * 2):
        results = []
        def enum_cb(hwnd, _):
            if win32gui.IsWindowVisible(hwnd):
                text = win32gui.GetWindowText(hwnd)
                if title_contains.lower() in text.lower():
                    results.append(hwnd)
            return True
        win32gui.EnumWindows(enum_cb, None)
        # Filter out non-top-level windows (ones with visible parents)
        top_level = [h for h in results if win32gui.GetParent(h) == 0]
        if top_level:
            return top_level[0]
        time.sleep(0.5)
    return None

def set_foreground(hwnd):
    """Bring a window to the foreground"""
    try:
        if win32gui.IsIconic(hwnd):
            win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
        win32gui.ShowWindow(hwnd, win32con.SW_SHOW)
        win32gui.SetForegroundWindow(hwnd)
        time.sleep(0.5)
    except:
        pass

def capture_window_by_title(title, output_path, timeout=15):
    """Find window by title, bring to front, capture it"""
    hwnd = find_window(title, timeout)
    if not hwnd:
        log(f"  [WARN] Window '{title}' not found!")
        return False
    try:
        set_foreground(hwnd)
        time.sleep(0.5)
        rect = win32gui.GetWindowRect(hwnd)
        x, y, w, h = rect
        # Clamp to screen dimensions
        screen_w, screen_h = pyautogui.size()
        x = max(0, x)
        y = max(0, y)
        w = min(w, screen_w)
        h = min(h, screen_h)
        if w > x and h > y:
            img = ImageGrab.grab(bbox=(x, y, w, h))
            img.save(output_path)
            log(f"  Saved: {output_path} ({w-x}x{h-y})")
            return True
    except Exception as e:
        log(f"  [WARN] Capture error: {e}")
    return False

def show_tray_context_menu():
    """
    Show the ServicePilot tray context menu by right-clicking the tray icon.
    Strategy: Find the hidden tray callback window and send WM_CONTEXTMENU.
    """
    # Try method 1: Find hidden H.NotifyIcon window and send context menu
    def find_tray_window():
        results = []
        def enum_cb(hwnd, _):
            cls_name = win32gui.GetClassName(hwnd)
            # H.NotifyIcon often uses its own window class
            if 'NotifyIcon' in cls_name or 'TrayIcon' in cls_name:
                results.append(hwnd)
            # Also look for hidden ServicePilot windows with no title
            text = win32gui.GetWindowText(hwnd)
            parent = win32gui.GetParent(hwnd)
            if parent == 0 and not text and win32gui.IsWindowVisible(hwnd) == False:
                cls = cls_name
                if 'H.NotifyIcon' in cls or cls.startswith('Window'):
                    results.append(hwnd)
            return True
        win32gui.EnumWindows(enum_cb, None)
        return results

    # Method 1: Send WM_CONTEXTMENU to tray windows
    tray_windows = find_tray_window()
    log(f"  Found {len(tray_windows)} potential tray windows")
    for hwnd in tray_windows:
        try:
            log(f"  Sending WM_CONTEXTMENU to hwnd=0x{hwnd:08x} class='{win32gui.GetClassName(hwnd)}'")
            win32gui.SendMessage(hwnd, win32con.WM_CONTEXTMENU, 0, 0)
            time.sleep(0.8)
        except:
            pass

    # Method 2: Try to find the tray icon by coordinates
    # The notification area is at the bottom-right of the taskbar
    screen_w, screen_h = pyautogui.size()
    
    # Try right-clicking at various positions on the taskbar notification area
    for try_y in [screen_h - 2, screen_h - 5, screen_h - 10, screen_h - 20, screen_h - 30]:
        for try_x in range(screen_w - 5, screen_w - 200, -30):
            # Check if there's a visible window here (part of taskbar)
            # Right-click and see if a popup menu appears
            pyautogui.moveTo(try_x, try_y)
            time.sleep(0.1)
            pyautogui.rightClick(try_x, try_y)
            time.sleep(0.5)
            # Check if a popup menu appeared (class #32768 = popup menu)
            popup = win32gui.FindWindow('#32768', None)
            if popup:
                log(f"  Tray menu opened at ({try_x}, {try_y})")
                return True
            # Press Escape to dismiss any menu
            pyautogui.press('esc')
            time.sleep(0.3)

    return False

def find_popup_menu(timeout=3):
    """Find a popup menu window (#32768 class)"""
    for _ in range(timeout * 4):
        hwnd = win32gui.FindWindow('#32768', None)
        if hwnd and win32gui.IsWindowVisible(hwnd):
            return hwnd
        time.sleep(0.25)
    return None

def dismiss_popup():
    """Press Escape to dismiss any open popup menu"""
    pyautogui.press('esc')
    time.sleep(0.5)

def click_tray_menu_item(item_text):
    """
    Click a specific tray menu item by text.
    The tray menu items are in the popup menu.
    We navigate using keyboard (arrow keys + Enter).
    """
    # After the tray menu is open, use keyboard to navigate
    # First, the title of the app is the first item or the status is first
    # Press Down arrow to find the item, then Enter
    pyautogui.press('down', interval=0.1)
    time.sleep(0.3)
    
    # Simple approach: press down to find item by going through the list
    # and checking what's selected
    # Actually, a more reliable method is to click at absolute positions
    # The popup menu is a list, we can find each item by position
    
    # For now, let's use a direct approach: try to find and click by screen position
    popup = find_popup_menu()
    if not popup:
        log("  [WARN] No popup menu found")
        return False
    
    try:
        # Get the menu rect
        rect = win32gui.GetWindowRect(popup)
        log(f"  Popup menu rect: {rect}")
        
        # The first menu item is typically at the top of the popup with some padding
        # Height of each menu item is roughly 20-24 pixels
        menu_x = rect[0] + 10  # Some padding from left
        menu_y = rect[1] + 5   # Some padding from top
        
        # We'd need to know the exact item positions
        # For now, simulate keyboard navigation
        # Find the service name in the menu
        # The menu structure is:
        # Status text (disabled)
        # --- Separator ---
        # Service 1 > (has submenu)
        # Service 2 > (has submenu)
        # ...
        # --- Separator ---
        # 新增服务
        # 管理服务
        # 管理模板
        # 复制给 AI 的帮助
        # --- Separator ---
        # 停止全部服务 (if any running)
        # 语言 >
        # 退出程序
        
    except Exception as e:
        log(f"  [WARN] Error accessing popup: {e}")
    
    return False

def launch_app():
    """Launch ServicePilot and wait for it to start"""
    log("Launching ServicePilot...")
    proc = subprocess.Popen(
        [APP_EXE],
        cwd=os.path.dirname(APP_EXE),
        shell=True
    )
    time.sleep(5)
    if wait_for_process(timeout=20):
        log("  Process started")
        time.sleep(2)  # Wait for tray icon to register
        return True
    log("  [WARN] Process did not start")
    return False

def prepare_config():
    """Prepare the config with demo services showing various states"""
    log("Preparing config...")
    
    # Backup existing config
    if os.path.exists(CONFIG_PATH):
        shutil.copy2(CONFIG_PATH, CONFIG_BAK_PATH)
        log(f"  Backed up to {CONFIG_BAK_PATH}")
    
    # Read current config
    if os.path.exists(CONFIG_PATH):
        with open(CONFIG_PATH, 'r', encoding='utf-8') as f:
            config = json.load(f)
    else:
        config = {"Version": 2, "Services": [], "ServiceTemplates": [], "Settings": {}}
    
    # Ensure Settings exist
    config.setdefault('Settings', {})
    config['Settings']['Language'] = 'zh-CN'
    config['Settings']['BuiltInTemplatesSeeded'] = True
    
    # Write config
    with open(CONFIG_PATH, 'w', encoding='utf-8') as f:
        json.dump(config, f, ensure_ascii=False, indent=2)
    log("  Config written")
    return config

def restore_config():
    """Restore the original config"""
    if os.path.exists(CONFIG_BAK_PATH):
        shutil.copy2(CONFIG_BAK_PATH, CONFIG_PATH)
        log(f"  Config restored from backup")
        os.remove(CONFIG_BAK_PATH)

def wait_for_app_ready(timeout=20):
    """Wait for the app to be ready (tray icon visible, windows responsive)"""
    log("Waiting for app to be ready...")
    time.sleep(3)
    # Check if the process is still running
    for i in range(timeout):
        r = os.system('tasklist /fi "imagename eq ServicePilot.exe" 2>nul | findstr ServicePilot >nul')
        if r == 0:
            if i > 0:
                log(f"  App ready after {i+1}s")
            return True
        time.sleep(1)
    return False

def take_cli_screenshots():
    """Take CLI screenshots by running commands and capturing terminal output"""
    log("\n=== CLI Screenshots ===")
    
    # We need to take screenshots of:
    # 1. ai-help-cli: ServicePilot.exe ai-help output
    # 2. status-doctor-cli: ServicePilot.exe doctor --json, ServicePilot.exe status all --json
    
    # Approach: Run the commands and save output as text that looks like a terminal
    # For actual screenshots, we'll open a new cmd window and capture it
    
    # Method: Start cmd.exe, run commands, take screenshot of the cmd window
    for lang_suffix, lang_title in [('zh', '命令提示符'), ('en', 'Administrator: Command Prompt')]:
        for cmd_name, cmd_args in [
            ('ai-help-cli', ['ai-help']),
            ('status-doctor-cli', ['doctor', '--json']),
        ]:
            output_path = os.path.join(OUTPUT_DIR, f'{cmd_name}-{lang_suffix}.png')
            
            # Start cmd.exe
            cmd_proc = subprocess.Popen(
                ['cmd.exe', '/k', f'"{APP_EXE}" {" ".join(cmd_args)}'],
                shell=True,
                creationflags=subprocess.CREATE_NEW_CONSOLE
            )
            time.sleep(2)
            
            # Wait for output to appear
            time.sleep(3)
            
            # Find the cmd window
            cmd_hwnd = find_window('命令提示符', timeout=5) or find_window('cmd', timeout=3)
            if cmd_hwnd:
                capture_window_by_title(win32gui.GetWindowText(cmd_hwnd), output_path)
            
            # Close cmd
            os.system('taskkill /f /im cmd.exe 2>nul')
            time.sleep(1)
    
    # Combined approach: run both commands in one terminal
    # Start cmd.exe, run first command, pause, run second command
    # Actually, let's do it simpler - create a batch script
    
    batch_content = f'@echo off\r\n"{APP_EXE}" ai-help\r\necho.\r\necho === STATUS ALL ===\r\n"{APP_EXE}" status all --json\r\necho.\r\necho === DOCTOR ===\r\n"{APP_EXE}" doctor --json\r\npause\r\n'
    batch_path = os.path.join(PROJECT_DIR, 'scripts', '_cli_screenshot_temp.bat')
    os.makedirs(os.path.dirname(batch_path), exist_ok=True)
    with open(batch_path, 'w') as f:
        f.write(batch_content)
    
    time.sleep(1)
    
    # Run the batch file in a new cmd window
    subprocess.Popen(['cmd.exe', '/k', batch_path], shell=True, creationflags=subprocess.CREATE_NEW_CONSOLE)
    time.sleep(5)
    
    # Take combined screenshot
    for lang_suffix in ['zh', 'en']:
        cmd_hwnd = find_window('命令提示符', timeout=5) or find_window('cmd', timeout=3)
        if cmd_hwnd:
            output_path = os.path.join(OUTPUT_DIR, f'status-doctor-cli-{lang_suffix}.png')
            capture_window_by_title(win32gui.GetWindowText(cmd_hwnd), output_path)
    
    # Cleanup
    time.sleep(2)
    os.system('taskkill /f /im cmd.exe 2>nul')
    if os.path.exists(batch_path):
        os.remove(batch_path)
    
    return True

def take_gui_screenshots(language):
    """Take GUI screenshots for a given language"""
    suffix = 'zh' if language == 'zh-CN' else 'en'
    
    log(f"\n{'='*60}")
    log(f"=== GUI Screenshots ({language}) ===")
    log(f"{'='*60}")
    
    # 1. TRAY MENU ─────────────────────────────────
    log("\n--- 1. Tray Menu ---")
    
    # Show the tray context menu
    if show_tray_context_menu():
        time.sleep(1)
        # Capture the whole screen to get the tray menu
        output_path = os.path.join(OUTPUT_DIR, f'tray-menu-{suffix}.png')
        # The tray menu is typically a small popup. Let's capture the whole taskbar area
        screen_w, screen_h = pyautogui.size()
        # Capture the bottom-right quadrant where the tray menu appears
        tray_area = (screen_w - 600, screen_h - 600, screen_w, screen_h)
        img = ImageGrab.grab(bbox=tray_area)
        img.save(output_path)
        log(f"  Saved: {output_path}")
        time.sleep(1)
        dismiss_popup()
        time.sleep(1)
    else:
        log("  [SKIP] Could not open tray menu")
    
    # 2. SERVICE MANAGER ───────────────────────────
    log("\n--- 2. Service Manager ---")
    
    # Open service manager from tray
    if show_tray_context_menu():
        time.sleep(0.8)
        # Use keyboard to navigate to "管理服务" / "Manage services"
        # The tray menu needs to be open first
        # ... then use arrow keys
        
        # For now, let's try a different approach:
        # Send keyboard shortcut to open service manager
        # Or use the app's window to trigger it
        pass
    
    dismiss_popup()
    
    # Try: Run ServicePilot.exe again with command line args
    # The app might support opening windows via named pipe
    # But looking at the code, it only supports status/doctor/help/tray commands
    
    # Alternative: Try to find and show an existing window or create one
    # The app doesn't have a main window - it's a tray-only app until you open windows
    # Let's try clicking on the tray icon to open the service manager
    
    # Actually, double-click on the tray icon might open the service manager
    screen_w, screen_h = pyautogui.size()
    # Try clicking on the notification area
    for try_y in range(screen_h - 10, screen_h - 40, -5):
        for try_x in range(screen_w - 30, screen_w - 150, -20):
            pos = (try_x, try_y)
            pyautogui.doubleClick(try_x, try_y)
            time.sleep(1)
            # Check if service manager appeared
            title = '管理服务' if language == 'zh-CN' else 'Manage'
            hwnd = find_window(title, timeout=2)
            if hwnd:
                log(f"  Service Manager opened at ({try_x}, {try_y})")
                output_path = os.path.join(OUTPUT_DIR, f'service-manager-{suffix}.png')
                capture_window_by_title(title, output_path)
                break
        else:
            continue
        break
    
    # 3. SERVICE EDITOR ────────────────────────────
    log("\n--- 3. Service Editor ---")
    
    # Find service manager window and click Edit on the first service
    title_sm = '管理服务' if language == 'zh-CN' else 'Manage'
    sm_hwnd = find_window(title_sm, timeout=3)
    if sm_hwnd:
        try:
            set_foreground(sm_hwnd)
            time.sleep(0.5)
            # The Edit button is in the Service Manager window
            # Use keyboard shortcut: might be Alt+E or Tab navigation
            # Or use pyautogui to click on the window
            rect = win32gui.GetWindowRect(sm_hwnd)
            log(f"  SM rect: {rect}")
            
            # Try to find Edit button and click it using keyboard
            # Tab to the first service's Edit button
            pyautogui.keyDown('alt')
            pyautogui.press('e')  # Alt+E might be Edit
            pyautogui.keyUp('alt')
            time.sleep(1)
            
            # Check if editor dialog appeared
            title_ed = 'Service configuration'
            ed_hwnd = find_window(title_ed, timeout=5)
            if ed_hwnd:
                output_path = os.path.join(OUTPUT_DIR, f'service-editor-{suffix}.png')
                capture_window_by_title(title_ed, output_path)
                time.sleep(0.5)
                # Close the editor
                win32gui.SendMessage(ed_hwnd, win32con.WM_CLOSE, 0, 0)
                time.sleep(1)
        except Exception as e:
            log(f"  [WARN] Editor error: {e}")
    
    # 4. LOG WINDOW ───────────────────────────────
    log("\n--- 4. Log Window ---")
    
    # Open log via tray menu
    if show_tray_context_menu():
        time.sleep(0.8)
        # Press down multiple times to reach a service's submenu
        for _ in range(1):
            pyautogui.press('down')
            time.sleep(0.3)
        # Right arrow to open submenu
        pyautogui.press('right')
        time.sleep(0.5)
        # Press down to go to "查看日志" / "View Logs"
        for _ in range(3):
            pyautogui.press('down')
            time.sleep(0.2)
        pyautogui.press('enter')
        time.sleep(1)
        dismiss_popup()
    
    # Check if log window opened
    title_log = '日志' if language == 'zh-CN' else 'Log'
    lw_hwnd = find_window(title_log, timeout=5)
    if lw_hwnd:
        output_path = os.path.join(OUTPUT_DIR, f'log-window-{suffix}.png')
        capture_window_by_title(title_log, output_path)
        time.sleep(0.5)
        # Don't close yet - we might need it
    
    # 5. AI HELP WINDOW ───────────────────────────
    log("\n--- 5. AI Help Window ---")
    
    # Close log window first
    if lw_hwnd:
        win32gui.SendMessage(lw_hwnd, win32con.WM_CLOSE, 0, 0)
        time.sleep(1)
    
    # Open AI help from tray
    if show_tray_context_menu():
        time.sleep(0.8)
        # Navigate to "复制给 AI 的帮助" / "Copy help for AI"
        # Press down multiple times to reach the item
        for _ in range(8):
            pyautogui.press('down')
            time.sleep(0.2)
        pyautogui.press('enter')
        time.sleep(1)
        dismiss_popup()
    
    # Check if AI help window opened
    title_ai = 'Copy ServicePilot help'
    ai_hwnd = find_window(title_ai, timeout=5)
    if ai_hwnd:
        output_path = os.path.join(OUTPUT_DIR, f'ai-help-{suffix}.png')
        capture_window_by_title(title_ai, output_path)
        time.sleep(0.5)
        win32gui.SendMessage(ai_hwnd, win32con.WM_CLOSE, 0, 0)
        time.sleep(1)
    else:
        log("  [WARN] AI Help window not found")
    
    return True

def switch_language(language):
    """Switch the app language via tray menu"""
    log(f"Switching language to {language}...")
    
    # Set config language
    if os.path.exists(CONFIG_PATH):
        with open(CONFIG_PATH, 'r', encoding='utf-8') as f:
            config = json.load(f)
        config.setdefault('Settings', {})
        config['Settings']['Language'] = language
        with open(CONFIG_PATH, 'w', encoding='utf-8') as f:
            json.dump(config, f, ensure_ascii=False, indent=2)
    
    # Restart app to apply
    kill_all_servicepilot()
    launch_app()
    time.sleep(3)
    return True

# ── Main ────────────────────────────────────────────────────────────────────

def main():
    log("=" * 60)
    log("ServicePilot v3.0.0 Screenshot Automation")
    log("=" * 60)
    
    # 0. Ensure build exists
    if not os.path.exists(APP_EXE):
        log(f"[ERROR] App not found at {APP_EXE}")
        log("Please build the project first: dotnet build -c Release")
        return False
    
    log(f"App: {APP_EXE}")
    log(f"Screenshots: {OUTPUT_DIR}")
    
    # 1. Kill any existing instances
    kill_all_servicepilot()
    
    # 2. Prepare config
    prepare_config()
    
    # 3. CHINESE SCREENSHOTS
    # Set language to Chinese
    switch_language('zh-CN')
    
    if wait_for_app_ready():
        take_gui_screenshots('zh-CN')
    
    # 4. ENGLISH SCREENSHOTS
    switch_language('en-US')
    
    if wait_for_app_ready():
        take_gui_screenshots('en-US')
    # 5. CLI SCREENSHOTS
    # CLI output is language-independent, just take once
    
    # 6. Cleanup
    kill_all_servicepilot()
    restore_config()
    
    log("\n=== Done! ===")
    return True

if __name__ == '__main__':
    main()
