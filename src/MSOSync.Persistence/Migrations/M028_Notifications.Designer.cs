// src/MSOSync.Persistence/Migrations/M028_Notifications.Designer.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Persistence;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("M028_Notifications")]
    partial class M028_Notifications
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            // Intentionally minimal — EF model snapshot carries authoritative state
        }
    }
}
