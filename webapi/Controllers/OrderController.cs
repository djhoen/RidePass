using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using webapi.Models;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Repositories.Data.OrderData;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IUserRepository _userRepository;

        public OrderController(IOrderRepository orderRepository, IUserRepository userRepository)
        {
            _orderRepository = orderRepository;
            _userRepository = userRepository;
        }

        [Authorize]
        [HttpGet("GetOrder")]
        public async Task<IActionResult> GetOrder([FromQuery] int id)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var order = await _orderRepository.GetOrder(id);

                return new ApiResponses().OkResult(order);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize]
        [HttpGet("GetUserOrders")]
        public async Task<IActionResult> GetUserOrders()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var orders = await _orderRepository.GetUserOrders(userId);

                return new ApiResponses().OkResult(orders);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("SearchOrders")]
        public async Task<IActionResult> SearchOrders([FromBody] SearchRequest request)
        {
            try
            {
                var searchOrdersRequest = new SearchOrdersRequest
                {
                    OrderId = request.OrderId,
                    Email = request.Email,
                    UserId = request.UserId,
                    CouponCode = request.CouponCode,
                    StatusIds = request.StatusIds,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    PaymentStatus = request.PaymentStatus,
                    StripePaymentId = request.StripePaymentId,
                    OrderSourceIds = request.OrderSourceIds
                };

                var orders = await _orderRepository.SearchOrders(searchOrdersRequest);

                return new ApiResponses().OkResult(orders);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("CreateOrderNote")]
        public async Task<IActionResult> CreateOrderNote([FromBody] CreateOrderNoteRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var orderNote = new OrderNote
                {
                    OrderId = request.OrderId,
                    Note = request.Note,
                    CreatedByUserId = userId
                };

                var result = await _orderRepository.CreateOrderNote(orderNote);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("UpdateOrder")]
        public async Task<IActionResult> UpdateOrder([FromBody] UpdateOrderRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var order = await _orderRepository.GetOrder(request.OrderId);
                if (request.OrderStatusId.HasValue)
                    order.OrderStatusId = request.OrderStatusId.Value;
                if (request.PaymentStatus != null)
                    order.PaymentStatus = request.PaymentStatus;

                await _orderRepository.UpdateOrder(order);

                return new ApiResponses().OkResult(order);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }
    }
}
