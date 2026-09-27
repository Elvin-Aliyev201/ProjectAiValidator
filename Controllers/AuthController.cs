using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using LookFutureApi.Models;
using LookFutureApi.Services;
using BCrypt.Net;

namespace LookFutureApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ISupabaseService _supabase;
    private readonly IConfiguration _config;

    public AuthController(ISupabaseService supabase, IConfiguration config)
    {
        _supabase = supabase;
        _config = config;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || 
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { success = false, error = "Tüm alanlar doldurulmalıdır." });
        }

        if (request.Password.Length < 6)
        {
            return BadRequest(new { success = false, error = "Şifrə ən azı 6 simvol olmalıdır." });
        }



        var existingUsername = await _supabase.GetUserByUsernameAsync(request.Username);
        if (existingUsername != null)
        {
            return BadRequest(new { success = false, error = "Bu istifadəçi adı artıq mövcuddur." });
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var newUser = await _supabase.CreateUserAsync(request.Username, passwordHash);

        if (newUser == null)
        {
            return StatusCode(500, new { success = false, error = "Qeydiyyat zamanı xəta baş verdi." });
        }

        var token = GenerateJwtToken(newUser);
        return Ok(new { success = true, data = new { token, username = newUser.Username, userId = newUser.Id } });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _supabase.GetUserByUsernameAsync(request.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { success = false, error = "İstifadəçi adı və ya şifrə yanlışdır." });
        }

        var token = GenerateJwtToken(user);
        return Ok(new { success = true, data = new { token, username = user.Username, userId = user.Id } });
    }

    private string GenerateJwtToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(double.Parse(_config["Jwt:ExpiryHours"] ?? "72")),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
