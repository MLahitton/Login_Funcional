using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

public class FacebookLoginRequest
{
    [Required(ErrorMessage = "El token de Facebook es obligatorio.")]
    public string AccessToken { get; set; } = string.Empty;
}
