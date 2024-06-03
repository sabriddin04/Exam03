namespace Domain.DTOs.NotificationDTOs;

public class GetNotificationDto
{
    public int Id { get; set; }
    public int MeetingId { get; set; }
    public int UserId { get; set; }
    public string Message { get; set; } = null!;
    public DateTime SendDate { get; set; }
    public DateTimeOffset CreateAt { get; set; }
    public DateTimeOffset UpdateAt { get; set; }
}