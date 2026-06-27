using System.Numerics;
using System.Diagnostics;
using Raylib_cs;

namespace Raylib3D
{
    /// <summary>
    /// Love2D-style fullscreen crash screen. Renders the exception message and a
    /// formatted, project-relative stack trace, and lets the user copy it (Ctrl+C)
    /// or quit (Esc).
    /// </summary>
    static class CrashHandler
    {
        // Love2D's signature blue (#181853-ish background)
        static readonly Color LoveBlue   = new Color(89, 157, 220, 255);
        static readonly Color LoveBlueBg = new Color(27, 27, 80, 255);

        /// <summary>
        /// Draws the crash screen and blocks until the user closes it.
        /// The four fonts are supplied by the caller; if any is unloaded
        /// (Texture.Id == 0) a default-font fallback is used.
        /// </summary>
        public static void ShowErrorScreen(
            Exception ex,
            Font font,
            Font font_bold,
            Font font_mono,
            Font font_mono_bold)
        {
            string exceptionText = BuildExceptionText(ex);   // [EXCEPTION]
            string traceText     = BuildTraceText(ex);       // [STACK_TRACE]
            string clipboard     = exceptionText + "\n\nTraceback\n" + traceText;

            // Make sure a window exists (the crash may have happened before InitWindow)
            if (!Raylib.IsWindowReady())
                Raylib.InitWindow(800, 600, "Error");

            // If fonts never loaded (e.g. crash before LoadFonts), fall back to default.
            if (font_mono.Texture.Id == 0)
            {
                Font def = Raylib.GetFontDefault();
                font = font_bold = font_mono = font_mono_bold = def;
            }

            // Unlock the mouse so the user can interact with the error screen.
            Raylib.EnableCursor();
            if (Raylib.IsCursorHidden())
                Raylib.ShowCursor();

            Raylib.SetExitKey(KeyboardKey.Null); // we handle Esc ourselves

            while (!Raylib.WindowShouldClose())
            {
                // Ctrl+C copies the traceback to the clipboard, like Love2D
                if (Raylib.IsKeyDown(KeyboardKey.LeftControl) && Raylib.IsKeyPressed(KeyboardKey.C))
                    Raylib.SetClipboardText(clipboard);

                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                    break;

                Raylib.BeginDrawing();
                Raylib.ClearBackground(LoveBlueBg);

                int margin = 70;
                int y = margin;
                int maxWidth = Raylib.GetScreenWidth() - margin * 2;

                // Title: bold
                Raylib.DrawTextEx(font_bold, "Error", new Vector2(margin, y), 30, 1, Color.White);
                y += 50;

                // [EXCEPTION]: monospaced
                foreach (string line in WrapTextMono(font_mono, exceptionText, 16, maxWidth))
                {
                    Raylib.DrawTextEx(font_mono, line, new Vector2(margin, y), 16, 0, Color.White);
                    y += 22;
                }

                y += 16; // gap before the Traceback heading

                // "Traceback" heading: bold
                Raylib.DrawTextEx(font_bold, "Traceback", new Vector2(margin, y), 22, 1, Color.White);
                y += 34;

                // [STACK_TRACE]: monospaced
                foreach (string line in WrapTextMono(font_mono, traceText, 16, maxWidth))
                {
                    Raylib.DrawTextEx(font_mono, line, new Vector2(margin, y), 16, 0, Color.White);
                    y += 22;
                }

                // Footer hint: regular
                int footerY = Raylib.GetScreenHeight() - 40;
                Raylib.DrawTextEx(font, "Press Esc to quit    |    Ctrl+C to copy",
                    new Vector2(margin, footerY), 14, 1, LoveBlue);

                Raylib.EndDrawing();
            }
        }

        // [EXCEPTION] — the exception type and message (plus any inner messages).
        static string BuildExceptionText(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            Exception? cur = ex;
            while (cur != null)
            {
                sb.AppendLine($"{cur.GetType().Name}: {cur.Message}");
                cur = cur.InnerException;
                if (cur != null) sb.AppendLine("--- caused by ---");
            }
            return sb.ToString().TrimEnd();
        }

        // Cached project root, resolved once by walking up from the running
        // binary's directory until a .csproj or .sln is found.
        static string? _projectRoot;
        static bool _projectRootResolved;

        static string? GetProjectRoot()
        {
            if (_projectRootResolved) return _projectRoot;
            _projectRootResolved = true;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (dir.EnumerateFiles("*.csproj").Any() ||
                    dir.EnumerateFiles("*.sln").Any())
                {
                    _projectRoot = dir.FullName;
                    return _projectRoot;
                }
                dir = dir.Parent;
            }
            return _projectRoot; // null if not found
        }

        // Make an absolute source path relative to the project root, if possible.
        static string RelativeScriptPath(string fullPath)
        {
            string? root = GetProjectRoot();
            if (root == null) return fullPath;
            try
            {
                string rel = Path.GetRelativePath(root, fullPath);
                // GetRelativePath returns the input unchanged if it can't relativize.
                return rel.StartsWith("..") ? fullPath : rel.Replace('\\', '/');
            }
            catch { return fullPath; }
        }

        // [STACK_TRACE] — formatted per-frame, walking inner exceptions too.
        //   at [METHOD]
        //       in '[SCRIPT_PATH]' line [LINE_NUMBER]
        static string BuildTraceText(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            Exception? cur = ex;
            while (cur != null)
            {
                var trace = new StackTrace(cur, fNeedFileInfo: true);
                foreach (var frame in trace.GetFrames() ?? Array.Empty<StackFrame>())
                {
                    var method = frame.GetMethod();
                    if (method == null) continue;

                    string methodName = method.DeclaringType != null
                        ? $"{method.DeclaringType.FullName}.{method.Name}"
                        : method.Name;

                    sb.AppendLine($"at {methodName}");

                    int line = frame.GetFileLineNumber();
                    string? file = frame.GetFileName();
                    bool hasLine = line > 0;
                    bool hasFile = !string.IsNullOrEmpty(file);

                    if (hasFile && hasLine)
                        sb.AppendLine($"\tin '{RelativeScriptPath(file!)}' line {line}");
                    else if (hasFile)
                        sb.AppendLine($"\tin '{RelativeScriptPath(file!)}'");
                    else if (hasLine)
                        sb.AppendLine($"\ton line {line}");
                }

                cur = cur.InnerException;
                if (cur != null) sb.AppendLine("--- caused by ---");
            }
            return sb.ToString().TrimEnd();
        }

        // Word-wrap using the mono font's measured width.
        static System.Collections.Generic.List<string> WrapTextMono(
            Font font_mono, string text, int fontSize, int maxWidth)
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (string rawLine in text.Replace("\r", "").Replace("\t", "    ").Split('\n'))
            {
                if (rawLine.Length == 0) { lines.Add(""); continue; }

                // Preserve leading indentation (e.g. the "    " before "on line"/"in").
                int indentLen = rawLine.Length - rawLine.TrimStart(' ').Length;
                string indent = rawLine.Substring(0, indentLen);

                string current = indent;
                foreach (string word in rawLine.TrimStart(' ').Split(' '))
                {
                    string test = current.Length == indentLen ? indent + word : current + " " + word;
                    if (Raylib.MeasureTextEx(font_mono, test, fontSize, 0).X > maxWidth && current.Length > indentLen)
                    {
                        lines.Add(current);
                        current = indent + word;
                    }
                    else current = test;
                }
                if (current.Length > indentLen) lines.Add(current);
            }
            return lines;
        }
    }
}
