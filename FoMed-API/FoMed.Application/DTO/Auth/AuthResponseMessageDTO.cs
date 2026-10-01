namespace FoMed.Application.DTO.Auth;

public static class AuthResponseMessageDTO
{
    public const string RegisterSuccess = "Đăng ký thành công.";
    public const string EmailAlreadyUsed = "Email đã được sử dụng.";
    public const string LoginSuccess = "Đăng nhập thành công.";
    public const string InvalidCredentials = "Email hoặc mật khẩu không đúng.";
    public const string AccountInactive = "Tài khoản đã bị khóa hoặc ngừng hoạt động.";
    public const string InvalidRefreshToken = "Refresh token không hợp lệ hoặc đã hết hạn.";
    public const string ForgotPasswordAccepted = "Nếu email tồn tại, hướng dẫn khôi phục mật khẩu sẽ được gửi.";
    public const string AccountAlreadyUsed = "TÃ i khoáº£n hoáº·c sá»‘ Ä‘iá»‡n thoáº¡i Ä‘Ã£ Ä‘Æ°á»£c sá»­ dá»¥ng.";
    public const string PatientLinkAmbiguous = "CÃ³ nhiá»u há»“ sÆ¡ vÃ£ng lai khÃ´ng thá»ƒ tá»± Ä‘á»™ng liÃªn káº¿t.";
}
