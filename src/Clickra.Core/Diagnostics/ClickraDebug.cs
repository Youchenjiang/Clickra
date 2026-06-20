using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Clickra.Core
{
    public static class ClickraDebug
    {
        private static readonly List<string> _lines = new();
        private static readonly object _lock = new();

        public static void Clear()
        {
            lock (_lock)
            {
                _lines.Clear();
            }
        }

        public static void LogMask(int page, double paraY0, double paraY1,
            double mx0, double my0, double mx1, double my1,
            double my1BeforeClamp, double renderedH)
        {
            string skipped = my0 >= my1 - 0.5 ? " -> SKIP" : "";
            lock (_lock)
            {
                _lines.Add($"P{page} MASK paraY=[{paraY0:F1},{paraY1:F1}] mask=[{mx0:F1},{my0:F1},{mx1:F1},{my1:F1}] beforeClamp={my1BeforeClamp:F1} rendH={renderedH:F1} h={(my1 - my0):F1}{skipped}");
            }
        }

        public static void LogRender(int page, double paraY0, double paraY1,
            double paraX0, double paraX1, bool clipped, double measuredH)
        {
            lock (_lock)
            {
                _lines.Add($"P{page} RENDER paraY=[{paraY0:F1},{paraY1:F1}] X=[{paraX0:F1},{paraX1:F1}] clipped={clipped} measuredH={measuredH:F1}");
            }
        }

        public static void SaveTo(string path)
        {
            lock (_lock)
            {
                File.WriteAllLines(path, _lines, Encoding.UTF8);
            }
        }

        public static IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lock)
                {
                    return _lines.ToList();
                }
            }
        }
    }
}
