using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace AffinitySetter.Utils;
internal static class CommandLineUtils
{
    // 缓存编译后的正则表达式，避免重复创建
    private static readonly ConcurrentDictionary<string, Regex?> _regexCache = new();
    private const int MaxCacheSize = 100;
    
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
                
                // 从缓存获取或创建正则表达式
                var regex = _regexCache.GetOrAdd(regexPattern, p =>
                {
                    try
                    {
                        return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
                    }
                    catch
                    {
                        return null;
                    }
                });
                
                // 防止缓存无限增长
                if (_regexCache.Count > MaxCacheSize)
                {
                    _regexCache.Clear();
                }
                
                return regex?.IsMatch(commandLine) ?? false;
            }
            catch (RegexMatchTimeoutException)
            {
                Console.WriteLine($"⚠️ Regex timeout: {pattern}");
                return false;
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