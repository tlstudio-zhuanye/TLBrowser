using System.Security.Cryptography;
using System.Text;

namespace TLBrowser;

/// <summary>
/// 零散的小工具函数。放在这里的东西满足两个条件：跟浏览器本身无关、
/// 又不够格单独开一个文件。工具箱里好几处都要用。
/// </summary>
internal static class Tools
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}<>?,.";

    /// <summary>
    /// 生成随机密码。用的是 <see cref="RandomNumberGenerator"/>，不是 Random
    /// —— Random 的种子可以推出来，拿它生成密码等于没生成。
    /// 每一类字符各保证至少一个，避免出现「16 位全是字母」这种看着像样的弱密码。
    /// </summary>
    public static string RandomPassword(int length, bool symbols)
    {
        length = Math.Clamp(length, 4, 128);
        var pool = Lower + Upper + Digits + (symbols ? Symbols : "");
        var sb = new StringBuilder(length);

        if (symbols)
        {
            sb.Append(Pick(Lower)).Append(Pick(Upper)).Append(Pick(Digits)).Append(Pick(Symbols));
        }
        else
        {
            sb.Append(Pick(Lower)).Append(Pick(Upper)).Append(Pick(Digits));
        }

        while (sb.Length < length) sb.Append(Pick(pool));

        // 头几个固定字符的位置也要打乱，否则每次都是「小写大写数字符号」开头
        var chars = sb.ToString().ToCharArray();
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    private static char Pick(string pool) =>
        pool[RandomNumberGenerator.GetInt32(pool.Length)];

    /// <summary>把字节数说成人话（1.23 GB 这种）。</summary>
    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return v.ToString(u == 0 ? "F0" : "F2",
            System.Globalization.CultureInfo.InvariantCulture) + " " + units[u];
    }

    /// <summary>把一长串地址缩成「看得懂是哪一站」的程度。</summary>
    public static string Shorten(string s, int max = 60) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
