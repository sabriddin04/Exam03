using System.Net;
using System.Text;
using Domain.DTOs.EmailDTOs;
using Domain.DTOs.NotificationDTOs;
using Domain.Entities;
using Domain.Filters;
using Domain.Responses;
using Infrastructure.Data;
using Infrastructure.Services.EmailService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit.Text;

namespace Infrastructure.Services.NotificationService;

public class NotificationService(ILogger<NotificationService> logger, DataContext context, IEmailService emailService) : INotificationService
{
    #region GetNotificationsAsync

    public async Task<PagedResponse<List<GetNotificationDto>>> GetNotificationsAsync(NotificationFilter filter)
    {
        try
        {
            logger.LogInformation("Starting method {GetNotificationsAsync} in time:{DateTime} ", "GetNotificationsAsync",
                DateTimeOffset.UtcNow);
            var Notifications = context.Notifications.AsQueryable();

            if (filter.SendDate != null)
                Notifications = Notifications.Where(x => x.SendDate >= filter.SendDate);

            var response = await Notifications.Select(x => new GetNotificationDto()
            {
                Message = x.Message,
                MeetingId = x.MeetingId,
                SendDate = x.SendDate,
                UserId = x.UserId,
                CreateAt = x.CreateAt,
                UpdateAt = x.UpdateAt,
                Id = x.Id,
            }).Skip((filter.PageNumber - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync();

            var totalRecord = await Notifications.CountAsync();

            logger.LogInformation("Finished method {GetNotificationsAsync} in time:{DateTime} ", "GetNotificationsAsync",
                DateTimeOffset.UtcNow);
            return new PagedResponse<List<GetNotificationDto>>(response, filter.PageNumber, filter.PageSize, totalRecord);
        }
        catch (Exception e)
        {
            logger.LogError("Exception {Exception}, time={DateTimeNow}", e.Message, DateTimeOffset.UtcNow);
            return new PagedResponse<List<GetNotificationDto>>(HttpStatusCode.InternalServerError, e.Message);
        }
    }

    #endregion

    #region GetNotificationByIdAsync

    public async Task<Response<GetNotificationDto>> GetNotificationByIdAsync(int NotificationId)
    {
        try
        {
            logger.LogInformation("Starting method {GetNotificationByIdAsync} in time:{DateTime} ", "GetNotificationByIdAsync",
                DateTimeOffset.UtcNow);

            var existing = await context.Notifications.Select(x => new GetNotificationDto()
            {
                Message = x.Message,
                MeetingId = x.MeetingId,
                SendDate = x.SendDate,
                UserId = x.UserId,
                CreateAt = x.CreateAt,
                UpdateAt = x.UpdateAt,
                Id = x.Id,
            }).FirstOrDefaultAsync(x => x.Id == NotificationId);

            if (existing is null)
            {
                logger.LogWarning("Could not find Notification with Id:{Id},time:{DateTimeNow}", NotificationId, DateTimeOffset.UtcNow);
                return new Response<GetNotificationDto>(HttpStatusCode.BadRequest, $"Not found Notification by id:{NotificationId}");
            }


            logger.LogInformation("Finished method {GetNotificationByIdAsync} in time:{DateTime} ", "GetNotificationByIdAsync",
                DateTimeOffset.UtcNow);
            return new Response<GetNotificationDto>(existing);
        }
        catch (Exception e)
        {
            logger.LogError("Exception {Exception}, time={DateTimeNow}", e.Message, DateTimeOffset.UtcNow);
            return new Response<GetNotificationDto>(HttpStatusCode.InternalServerError, e.Message);
        }
    }

    #endregion

    #region CreateNotificationAsync

    public async Task<Response<string>> CreateNotificationAsync(CreateNotificationDto createNotification)
    {
        try
        {
            logger.LogInformation("Starting method {CreateNotificationAsync} in time:{DateTime} ", "CreateNotificationAsync",
                DateTimeOffset.UtcNow);

            var newNotification = new Notification()
            {
                Message = createNotification.Message,
                MeetingId = createNotification.MeetingId,
                UserId = createNotification.UserId,
                SendDate = DateTime.UtcNow,
                CreateAt = DateTimeOffset.UtcNow,
                UpdateAt = DateTimeOffset.UtcNow,
            };

            await context.Notifications.AddAsync(newNotification);
            await context.SaveChangesAsync();

            logger.LogInformation("Finished method {CreateNotificationAsync} in time:{DateTime} ", "CreateNotificationAsync",
                DateTimeOffset.UtcNow);
            return new Response<string>($"Successfully created Notification by Id:{newNotification.Id}");
        }
        catch (Exception e)
        {
            logger.LogError("Exception {Exception}, time={DateTimeNow}", e.Message, DateTimeOffset.UtcNow);
            return new Response<string>(HttpStatusCode.InternalServerError, e.Message);
        }
    }

    #endregion

    #region SendNotificationAsync

public async Task<Response<string>> SendNotificationAsync()
{
    try
    {
        logger.LogInformation("Begin method SendNotificationAsync at {DateTime}", DateTime.UtcNow);

        var upcomingMeetings = await context.Meetings
            .Where(m => (m.StartDate - DateTime.UtcNow) > TimeSpan.FromDays(1))
            .Include(m => m.Notifications)
            .ToListAsync();

        if (!upcomingMeetings.Any())
        {
            logger.LogInformation("No upcoming meetings found at {DateTime}", DateTime.UtcNow);
            return new Response<string>("No upcoming meetings found.");
        }

        var userNotifications = new Dictionary<string, List<string>>();

        foreach (var meeting in upcomingMeetings)
        {
            foreach (var notification in meeting.Notifications)
            {
                var user = await context.Users.FindAsync(notification.UserId);
                if (user != null)
                {
                    if (!userNotifications.ContainsKey(user.Email))
                    {
                        userNotifications[user.Email] = new List<string>();
                    }

                    userNotifications[user.Email].Add(notification.Message);
                }
            }
        }

        foreach (var userNotification in userNotifications)
        {
            var email = userNotification.Key;
            var messages = userNotification.Value;
            var notificationsInfo = new StringBuilder();
            notificationsInfo.Append("<div style='font-family:Arial,sans-serif;color:#333;'><h2>Your upcoming meetings</h2>");

            foreach (var message in messages)
            {
                notificationsInfo.AppendFormat("<p>{0}</p>", message);
            }

            notificationsInfo.Append("</div>");

            var emailMessage = new EmailMessageDto(
                new[] { email }, 
                "Your upcoming meetings in the next 1 days",
                notificationsInfo.ToString()
            );

            await emailService.SendEmail(emailMessage, TextFormat.Html);
        }

        logger.LogInformation("Completed method SendNotificationAsync at {DateTime}", DateTime.UtcNow);
        return new Response<string>("Notifications sent successfully!");
    }
    catch (Exception ex)
    {
        logger.LogError("Exception occurred: {ExceptionMessage}, at {DateTimeNow}", ex.Message, DateTimeOffset.UtcNow);
        return new Response<string>(HttpStatusCode.InternalServerError, ex.Message);
    }
}
    #endregion
}