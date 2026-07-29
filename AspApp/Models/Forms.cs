using System.ComponentModel.DataAnnotations;

namespace AspApp.Models;

public class Authentication_Login_FormModel
{
    public string? ReturnUrl { get; set; }

    [StringLength(256)]
    public string UsernameOrEmail { get; set; } = string.Empty;

    [StringLength(256, MinimumLength = 5)]
    public string Password { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? CfTurnstileResponse { get; set; }
}

public class Authentication_LoginWithProvider_FormModel
{
    public string? ReturnUrl { get; set; }
}