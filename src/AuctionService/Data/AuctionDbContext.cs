using AuctionService.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Data;

public class AuctionDbContext: DbContext
{
    public AuctionDbContext(DbContextOptions options) : base(options){} // Provider of the database is provided here in the options

    public DbSet<Auction> Auctions { get; set; }
}