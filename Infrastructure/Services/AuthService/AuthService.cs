using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Domain.Constants;
using Domain.DTOs.AuthDTOs;
using Domain.DTOs.EmailDTOs;
using Domain.Entities;
using Domain.Responses;
using Infrastructure.Data;
using Infrastructure.Services.EmailService;
using Infrastructure.Services.HashService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MimeKit.Text;

namespace Infrastructure.Services.AuthService;

public class AuthService(DataContext context, ILogger<AuthService> logger, IEmailService emailService,
    IHashService hashService, IConfiguration configuration) : IAuthService
{
    #region Register
  public async Task<Response<string>> Register(RegisterDto model)
    {
        try
        {
            var result = await context.Users.FirstOrDefaultAsync(x => x.Username == model.UserName);
            if (result != null) return new Response<string>(HttpStatusCode.BadRequest, "Such a user already exists!");
            var user = new User()
            {
                Username = model.UserName,
                Email = model.Email,
                Phone = string.Empty,
                CreateAt = DateTimeOffset.UtcNow,
                UpdateAt= DateTimeOffset.UtcNow,
                Password = hashService.ConvertToHash(model.Password)
            };

            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            var existingUser = await context.Users.FirstOrDefaultAsync(x => x.Username == model.UserName);
            var existingRole = await context.Roles.FirstOrDefaultAsync(x => x.Name == Roles.User);
            if (existingUser is not null && existingRole is not null)
            {
                var userRole = new UserRole()
                {
                    RoleId = existingRole.Id,
                    UserId = existingUser.Id,
                    Role = existingRole,
                    User = existingUser,
                    UpdateAt = DateTimeOffset.UtcNow,
                    CreateAt = DateTimeOffset.UtcNow
                };
                await context.UserRoles.AddAsync(userRole);
                await context.SaveChangesAsync();
            }


            return new Response<string>($"Done.  Your registered by id {user.Id}");
        }
        catch (Exception e)
        {
            return new Response<string>(HttpStatusCode.InternalServerError, e.Message);
        }
    }

    public async Task<Response<string>> Login(LoginDto model)
    {
        try
        {
            var user = await context.Users.FirstOrDefaultAsync(x=>x.Username==model.UserName && x.Password==hashService.ConvertToHash(model.Password));
            if (user is not null)
            {
                    return new Response<string>(await GenerateJwtToken(user));
            }

            return new Response<string>(HttpStatusCode.BadRequest, "Your username or password is incorrect!!!");
        }
        catch (Exception e)
        {
            return new Response<string>(HttpStatusCode.InternalServerError, e.Message);
        }
    }


    #endregion

    #region GenerateJwtToken

    private async Task<string> GenerateJwtToken(User user)
    {
        var key = Encoding.UTF8.GetBytes(configuration["JWT:Key"]!);
        var securityKey = new SymmetricSecurityKey(key);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>()
        {
            new(JwtRegisteredClaimNames.Sid, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.Username!),
            new(JwtRegisteredClaimNames.Email, user.Email!),
        };
        //add roles

        var roles = await context.UserRoles.Where(x => x.UserId == user.Id).Include(x => x.Role)
            .Select(x => x.Role).ToListAsync();
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role!.Name));
            var roleClaims = await context.RoleClaims.Where(x => x.RoleId == role.Id).ToListAsync();
            foreach (var roleClaim in roleClaims)
            {
                claims.Add(new Claim("Permissions", roleClaim.ClaimValue));
            }
        }

        var token = new JwtSecurityToken(
            issuer: configuration["JWT:Issuer"],
            audience: configuration["JWT:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );

        var securityTokenHandler = new JwtSecurityTokenHandler();
        var tokenString = securityTokenHandler.WriteToken(token);
        return tokenString;
    }

    #endregion

    #region ResetPassword

    public async Task<Response<string>> ResetPassword(ResetPasswordDto resetPasswordDto)
    {
        try
        {
            logger.LogInformation("Starting method {ResetPassword} in time {DateTime}", "ResetPassword",
                DateTimeOffset.UtcNow);
            var existing = await context.Users.FirstOrDefaultAsync(x => x.Email == resetPasswordDto.Email);
            if (existing is null)
            {
                logger.LogWarning("Not found user with {Email},time={DateTimeNow}", resetPasswordDto.Email,
                    DateTimeOffset.UtcNow);
                return new Response<string>(HttpStatusCode.BadRequest, $"Not Found User with {resetPasswordDto.Email}");
            }


            var timeCode = DateTimeOffset.UtcNow - existing.CodeTime;
            var year = DateTimeOffset.UtcNow.Year - existing.CodeTime.Year;

            if (timeCode.Days == 0 && timeCode.Hours == 0 && timeCode.Minutes <= 2 && year == 0)
            {
                existing.Password = hashService.ConvertToHash(resetPasswordDto.Password);
                await context.SaveChangesAsync();

                logger.LogInformation("Finished method {ResetPassword} in time {DateTime}", "ResetPassword",
                    DateTimeOffset.UtcNow);
                return new Response<string>("Success");
            }

            logger.LogWarning("Failed {Code},time={DateTimeNow}", resetPasswordDto.Code, DateTimeOffset.UtcNow);
            return new Response<string>(HttpStatusCode.BadRequest, $"Failed {resetPasswordDto.Code}");
        }
        catch (Exception e)
        {
            logger.LogError("Exception {Exception} in time:{DateTime} ", e.Message, DateTimeOffset.UtcNow);
            return new Response<string>(HttpStatusCode.InternalServerError, e.Message);
        }
    }

    #endregion

    #region ForgotPasswordCodeGenerator

    public async Task<Response<string>> ForgotPasswordCodeGenerator(ForgotPasswordDto forgotPasswordDto)
    {
        try
        {
            logger.LogInformation("Starting method {ForgotPasswordTokenGenerator} in time {DateTime}", "Register",
                DateTimeOffset.UtcNow);
            var existing = await context.Users.FirstOrDefaultAsync(x => x.Email == forgotPasswordDto.Email);
            if (existing is null)
            {
                logger.LogWarning("Not found user with email {Email} ,time={DateTimeNow} ", forgotPasswordDto.Email,
                    DateTimeOffset.UtcNow);
                return new Response<string>(HttpStatusCode.BadRequest,
                    $"Not Found User with email {forgotPasswordDto.Email}");
            }

            var random = new Random();

            existing.Code = random.Next(1000, 10000).ToString();
            existing.CodeTime = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync();


            await emailService.SendEmail(new EmailMessageDto(new[] { forgotPasswordDto.Email }, "reset password",
                $"<h1>{existing.Code}</h1>"), TextFormat.Html);


            logger.LogInformation("Finished method {ForgotPasswordTokenGenerator} in time {DateTime}", "Register",
                DateTimeOffset.UtcNow);
            return new Response<string>("Success");
        }
        catch (Exception e)
        {
            logger.LogError("Exception {Exception} in time:{DateTime} ", e.Message, DateTimeOffset.UtcNow);
            return new Response<string>(HttpStatusCode.InternalServerError, e.Message);
        }
    }

    #endregion

    #region ChangePassword

    public async Task<Response<string>> ChangePassword(ChangePasswordDto passwordDto, int userId)
    {
        try
        {
            logger.LogInformation("Starting method {ChangePassword} in time {DateTime}", "ChangePassword",
                DateTimeOffset.UtcNow);

            var existing = await context.Users.FirstOrDefaultAsync(x => x.Id == userId);
            if (existing is null)
            {
                logger.LogWarning("User not found,in time {DateTime}", DateTimeOffset.Now);
                return new Response<string>(HttpStatusCode.BadRequest, "User not found");
            }

            if (existing.Password != hashService.ConvertToHash(passwordDto.OldPassword))
            {
                logger.LogWarning("Failed old Password,in time {DateTime}", DateTimeOffset.Now);
                return new Response<string>(HttpStatusCode.BadRequest, "Failed old Password");
            }

            existing.Password = hashService.ConvertToHash(passwordDto.Password);
            await context.SaveChangesAsync();

            logger.LogInformation("Finished method {ChangePassword} in time {DateTime}", "ChangePassword",
                DateTimeOffset.UtcNow);
            return new Response<string>("Successfully changed password");
        }
        catch (Exception e)
        {
            logger.LogError("Exception {Exception} in time:{DateTime} ", e.Message, DateTimeOffset.UtcNow);
            return new Response<string>(HttpStatusCode.InternalServerError, e.Message);
        }
    }

    #endregion

    #region DeleteAccount

    public async Task<Response<string>> DeleteAccount(int userId)
    {
        try
        {
            logger.LogInformation("Starting method {DeleteAccount} in time:{DateTime} ", "DeleteAccount",
                DateTimeOffset.UtcNow);

            var user = await context.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();
            logger.LogInformation("Finished method {DeleteAccount} in time:{DateTime} ", "DeleteAccount",
                DateTimeOffset.UtcNow);
            return user == 0
                ? new Response<string>(HttpStatusCode.BadRequest, $"User not found by Id:{userId}")
                : new Response<string>("Successfully deleted account");
        }
        catch (Exception e)
        {
            logger.LogError("Exception {Exception}, time={DateTimeNow}", e.Message, DateTimeOffset.UtcNow);
            return new Response<string>(HttpStatusCode.InternalServerError, e.Message);
        }
    }

    #endregion
}