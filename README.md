# WARDOGS 诸元 Overlay

只截取游戏窗口画面，绝不读取游戏内存、不注入、不 Hook。

## 下载

[**点击下载 WardogsFDC-win64.zip**](https://github.com/maydaysuper/wardogs-fdc/releases/latest/download/WardogsFDC-win64.zip)（约 99 MB）

解压后运行 `WardogsFDC.exe`。

## 第一次使用

1. 游戏改成无边框窗口化（独占全屏截不到）
2. 设置里可填 xAI / OpenAI API Key（可选，自动识图建议填写）。Key 只存在本机
3. 点「截取当前画面」，拖两个框：炮位坐标区、目标坐标区
4. 保存校准 → 开始识别。1080 / 1440 / 4K 按窗口比例缩放
5. 没有 Key：复制两行坐标到剪贴板，或手填后点「立刻解算」

## 快捷键

| 组合 | 作用 |
| --- | --- |
| Ctrl+Shift+O | 开 / 停识别 |
| Ctrl+Shift+1 | L81 |
| Ctrl+Shift+2 | SPH-2 |
| Ctrl+Shift+P | 鼠标穿透 |

## 安全边界

- Chromium `desktopCapturer` 抓已经画在屏幕上的像素
- 不调用 `ReadProcessMemory` / `WriteProcessMemory`
- 不 DLL 注入、不 `SetWindowsHookEx`
