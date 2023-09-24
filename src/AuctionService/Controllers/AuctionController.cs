using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using AuctionService.Data;
using AuctionService.Dtos;
using AuctionService.Entities;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

[ApiController]
[Route("api/auctions")]
public class AuctionController: ControllerBase
{
    private readonly AuctionDbContext _context;
    private readonly IMapper _mapper;
    private readonly IPublishEndpoint _publishEndpoint;

    public AuctionController(AuctionDbContext auctionDbContext, IMapper mapper, IPublishEndpoint  publishEndpoint)
    {
        _context = auctionDbContext;
        _mapper = mapper;
        _publishEndpoint = publishEndpoint;
    }

    [HttpGet]
    public async Task<ActionResult<List<AuctionDto>>> GetAll(string date)
    {
        var query = _context.Auctions.OrderBy(x => x.Item.Make).AsQueryable();

        if (!string.IsNullOrEmpty(date))
        {
            query = query.Where(x => x.UpdatedAt.CompareTo(DateTime.Parse(date).ToUniversalTime()) > 0);
        }
        
        var auctions = await _context.Auctions
            .Include(x=>x.Item)
            .OrderBy(x=>x.Item.Make)
            .ToListAsync();

        return await query.ProjectTo<AuctionDto>(_mapper.ConfigurationProvider).ToListAsync();
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<AuctionDto>> GetAuctionById(Guid id)
    {
        var auction = await _context.Auctions
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.Id == id);
       
        if (auction == null) return NotFound("Item not found");
        
        return _mapper.Map<AuctionDto>(auction);
    }
    
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<AuctionDto>> CreateAuction(CreateAuctionDto auctionDto)
    {
        var auction = _mapper.Map<Auction>(auctionDto); // Its act like a JS spread operator
        auction.Seller = User?.Identity?.Name;  // Identity to inject current user into our request
        _context.Auctions.Add(auction);

        var newAuction = _mapper.Map<AuctionDto>(auction);
        await _publishEndpoint.Publish(_mapper.Map<AuctionCreated>(newAuction));
        
        var result = await _context.SaveChangesAsync() > 0;
        
        if (!result)
        {
            BadRequest("something went wrong, don't saved to Db");
        }

        return CreatedAtAction
            (nameof(GetAuctionById), new { auction.Id }, newAuction);
    }
    [Authorize]
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateAuction(Guid id,UpdateAuctionDto updateAuctionDto)
    {
        var auction = await _context.Auctions
            .Include(x=>x.Item)
            .FirstOrDefaultAsync(x=>x.Id==id);
        
        if (auction==null)
        {
            return BadRequest("auction not found with Id");
        }

        if (auction!.Seller != User?.Identity?.Name)
        {
           return Forbid(); 
        }

        auction.Item.Make = updateAuctionDto.Make ?? auction.Item.Make;
        auction.Item.Model = updateAuctionDto.Model ?? auction.Item.Model;
        auction.Item.Color = updateAuctionDto.Color ?? auction.Item.Color;
        auction.Item.Mileage = updateAuctionDto.Mileage ?? auction.Item.Mileage;
        auction.Item.Year = updateAuctionDto.Year ?? auction.Item.Year;
        
        await _publishEndpoint.Publish(_mapper.Map<AuctionUpdated>(auction));
        
        var result = await _context.SaveChangesAsync() > 0;

        if (result) return Ok();
        
        return BadRequest("problem saving changes");
    }
    
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteAuction(Guid id)
    {
        var auction = await _context.Auctions
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.Id == id);
        
        if (auction == null) return NotFound("Item not found");
        
        if (auction!.Seller != User?.Identity?.Name) return Forbid(); 
        
        // from here we need to emmit the event to delete the auction from DB
        await _publishEndpoint.Publish<AuctionDeleted>(new { Id = auction.Id.ToString()}); // event emmited to RabitMQ
        _context.Auctions.Remove(auction);
        
      var result =  await  _context.SaveChangesAsync() > 0;
      if (!result) return BadRequest("something went wrong with deletion");
      
        return new OkObjectResult($"Item with {id} delete successfully");
    }

}