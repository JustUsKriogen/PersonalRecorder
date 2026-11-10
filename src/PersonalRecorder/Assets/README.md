# Personal Recorder artwork

`Logo.png` is the original transparent logo created for this application with the image-generation tool: a mint capture frame, red recording dot, and bookmark notch.

`PersonalRecorder.ico` packages that same artwork at 16, 24, 32, 48, 64, 128, and 256 pixels for Windows. Its alpha transparency is preserved. Both assets are embedded in the WPF application; the ICO is also used as the executable's native icon.

The folder glyph is a small WPF vector path in `MainWindow.xaml`, so it stays sharp at different display scales.
