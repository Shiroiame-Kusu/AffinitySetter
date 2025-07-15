using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace AffinitySetter.Utils;
internal static class CommandLineUtils
{
    public static string? GetCommandLine(int pid)
    {
        try
        {
            string cmdlinePath = $"/proc/{pid}/cmdline";
            if (!File.Exists(cmdlinePath))
                return null;
            
            // 读取命令行数据（以null字节分隔）
            byte[] data = File.ReadAllBytes(cmdlinePath);
            if (data.Length == 0)
                return null;
            
            // 将null字节替换为空格
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == 0)
                    data[i] = (byte)' ';
            }
            
            return Encoding.UTF8.GetString(data).Trim();
        }
        catch
        {
            return null;
        }
    }

    public static bool IsMatch(string? commandLine, string pattern, bool isRegex)
    {
        if (commandLine == null) 
            return false;
        
        if (isRegex)
        {
            try
            {
                // 去掉正则表达式的首尾斜杠
                string regexPattern = pattern[1..^1];
                return Regex.IsMatch(commandLine, regexPattern, RegexOptions.IgnoreCase);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Regex error: {pattern} - {ex.Message}");
                return false;
            }
        }
        
        // 普通文本匹配（不区分大小写）
        return commandLine.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}