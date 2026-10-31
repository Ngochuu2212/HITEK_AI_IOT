using System.Globalization;
using System.Text;

namespace Hitek.BackendApi.Common;

// Kiểm tra và đặt tên cho video tải lên làm nguồn camera mô phỏng.
public static class VideoFileStore
{
    // Đổi tên người dùng đặt thành tên file an toàn: bỏ dấu tiếng Việt, chỉ giữ chữ, số, '.',
    // '-', '_'. Edge Gateway và Backend tìm file theo đúng tên này.
    public static string SafeName(string original)
    {
        var name = Path.GetFileName(original.Replace('\\', '/')).Trim();
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(name).Replace('đ', 'd').Replace('Đ', 'D');

        var builder = new StringBuilder();
        foreach (var c in stem.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }
        var safeStem = builder.ToString().Trim('.', '_');
        if (safeStem.Length > 100) safeStem = safeStem[..100];
        return (safeStem.Length == 0 ? "video" : safeStem) + extension;
    }

    // Đuôi file có thể đặt tuỳ ý, nên đối chiếu thêm chữ ký ở đầu file để chắc là video.
    public static bool LooksLikeVideo(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 12)
        {
            var box = Encoding.ASCII.GetString(head[4..8]);
            if (box is "ftyp" or "moov" or "mdat" or "free" or "wide" or "skip") return true;      // MP4, MOV
            if (head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("AVI "u8)) return true; // AVI
            if (head[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 })) return true;          // MKV
        }
        return head.Length > 188 && head[0] == 0x47 && head[188] == 0x47;                           // MPEG-TS
    }
}
