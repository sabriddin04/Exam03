namespace Domain.DTOs.UserDTOs;

public class UpdateUserDto
{
    public required string Username { get; set; } 
    public required string Email { get; set; } 
    public required string Phone { get; set; } 
}