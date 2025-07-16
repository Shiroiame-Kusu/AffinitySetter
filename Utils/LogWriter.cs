using System;
using System.IO;
using System.Text;

namespace AffinitySetter.Utils;

internal class LogWriter : TextWriter
{
    private readonly TextWriter _originalOut;
    private readonly StreamWriter _logFileWriter;
    private readonly object _lock = new();

    public LogWriter(string logFilePath)
    {
        _originalOut = Console.Out;
        _logFileWriter = new StreamWriter(logFilePath, append: true, encoding: Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void WriteLine(string? value)
    {
        lock (_lock)
        {
            string line = value ?? string.Empty;
            _originalOut.WriteLine(line);
            _logFileWriter.WriteLine(line);
        }
    }

    public override void Write(char value)
    {
        lock (_lock)
        {
            _originalOut.Write(value);
            _logFileWriter.Write(value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logFileWriter?.Dispose();
        }
        base.Dispose(disposing);
    }
}

