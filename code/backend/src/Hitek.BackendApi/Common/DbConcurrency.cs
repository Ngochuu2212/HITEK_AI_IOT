using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hitek.BackendApi.Common;

public static class DbConcurrency
{
    // 23505 = unique_violation. Kiểm tra trước khi ghi vẫn có kẽ hở: hai request trùng
    // nhau đến cùng lúc thì cả hai đều thấy "chưa tồn tại" rồi cùng ghi. Ràng buộc duy
    // nhất dưới database mới là chốt chặn thật, ở đây chỉ nhận diện để trả về êm thay vì lỗi 500.
    public static bool IsUniqueViolation(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    // 23503 = foreign_key_violation. Gặp khi aiEventId trỏ tới một dòng ai_event chưa
    // tồn tại — bảng đó do AI Service sở hữu nên Backend không kiểm tra trước được,
    // chỉ nhận diện lỗi để trả về thông báo rõ nghĩa thay vì lỗi hệ thống.
    public static bool IsForeignKeyViolation(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23503" };

    // Bảng event có nhiều khoá ngoại sang bảng của AI Service (ai_event, camera). Cần biết
    // đúng ràng buộc nào bị vi phạm để báo cho bên gửi biết sai ở trường nào.
    public static bool ViolatesConstraintOn(this DbUpdateException ex, string tableName) =>
        ex.InnerException is PostgresException { SqlState: "23503", ConstraintName: { } name } &&
        name.Contains(tableName, StringComparison.OrdinalIgnoreCase);

    // Câu lệnh SQL thuần (ExecuteSqlRaw) ném thẳng PostgresException, không bọc trong
    // DbUpdateException như khi SaveChanges.
    public static bool IsRawUniqueViolation(this Exception ex) =>
        ex is PostgresException { SqlState: "23505" } || ex.InnerException is PostgresException { SqlState: "23505" };
}
