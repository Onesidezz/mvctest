using Microsoft.EntityFrameworkCore;
using mvctest.Models;

namespace mvctest.Context
{
    public class ContentManagerContext:DbContext
    {
        public ContentManagerContext(DbContextOptions<ContentManagerContext> options)
           : base(options)
        {
        }

        public DbSet<UserAccessLog> UserAccessLog { get; set; }
    }
}
