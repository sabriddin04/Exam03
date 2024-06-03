using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

public class DataContext : DbContext
{
    public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
        
    }
    public DbSet<User> Users { get; set; } 
    public DbSet<UserRole> UserRoles { get; set; } 
    public DbSet<Role> Roles { get; set; } 
    public DbSet<RoleClaim> RoleClaims { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<Meeting> Meetings { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        base.OnModelCreating(modelBuilder);
    }
}