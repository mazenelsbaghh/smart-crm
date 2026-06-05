using Microsoft.EntityFrameworkCore;
using Shared.Security;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Shared.Infrastructure
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(AppDbContext context, IPasswordHasher passwordHasher)
        {
            Console.WriteLine("🌱 Seeding database...");

            // 1. Seed Default Project
            var defaultProjectId = Guid.Parse("d3b07384-d113-4a15-bbf9-000000000000");
            var projectExists = await context.Projects.AnyAsync(p => p.Id == defaultProjectId);
            if (!projectExists)
            {
                var defaultProject = new Modules.Projects.Domain.Project
                {
                    Id = defaultProjectId,
                    Name = "Default Project",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                context.Projects.Add(defaultProject);
                await context.SaveChangesAsync();
                Console.WriteLine("✅ Default Project seeded.");
            }

            // 2. Seed Project Settings
            var settingsExists = await context.ProjectSettings.IgnoreQueryFilters().AnyAsync(s => s.ProjectId == defaultProjectId);
            if (!settingsExists)
            {
                var defaultSettings = new Modules.Projects.Domain.ProjectSettings
                {
                    ProjectId = defaultProjectId,
                    AiAutoReplyEnabled = true,
                    Timezone = "UTC",
                    GeminiApiKey = "mock_test_key",
                    UpdatedAt = DateTime.UtcNow
                };
                context.ProjectSettings.Add(defaultSettings);
                await context.SaveChangesAsync();
                Console.WriteLine("✅ Project Settings seeded.");
            }

            // 3. Seed Default User
            var userEmail = "admin@smartcore.com";
            var userExists = await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == userEmail);
            if (!userExists)
            {
                var adminUser = new Modules.Auth.Domain.User
                {
                    Id = Guid.NewGuid(),
                    Email = userEmail,
                    PasswordHash = passwordHasher.HashPassword("Password123"),
                    Role = "Owner",
                    ProjectId = defaultProjectId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                context.Users.Add(adminUser);
                await context.SaveChangesAsync();
                Console.WriteLine("✅ Default Admin User seeded (admin@smartcore.com / Password123).");
            }

            Console.WriteLine("🌱 Seeding complete.");
        }
    }
}
