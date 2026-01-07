using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MNBEMART.Services;

namespace MNBEMART.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ForecastingController : ControllerBase
    {
        private readonly IDemandForecastingService _forecastingService;
        private readonly IOptimalOrderQuantityService _eoqService;
        private readonly IStockoutPredictionService _stockoutService;

        public ForecastingController(
            IDemandForecastingService forecastingService,
            IOptimalOrderQuantityService eoqService,
            IStockoutPredictionService stockoutService)
        {
            _forecastingService = forecastingService;
            _eoqService = eoqService;
            _stockoutService = stockoutService;
        }

        [HttpGet("forecast/{materialId}/{warehouseId}")]
        public async Task<IActionResult> GetForecast(int materialId, int warehouseId, [FromQuery] int monthsAhead = 1, [FromQuery] string? method = null)
        {
            try
            {
                var forecast = string.IsNullOrEmpty(method)
                    ? await _forecastingService.ForecastAsync(materialId, warehouseId, monthsAhead)
                    : await _forecastingService.ForecastWithMethodAsync(materialId, warehouseId, method, monthsAhead);

                return Ok(forecast);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("forecast/all/{warehouseId}")]
        public async Task<IActionResult> GetAllForecasts(int warehouseId, [FromQuery] int monthsAhead = 1)
        {
            try
            {
                var forecasts = await _forecastingService.ForecastAllMaterialsAsync(warehouseId, monthsAhead);
                return Ok(forecasts);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("eoq/{materialId}/{warehouseId}")]
        public async Task<IActionResult> GetEOQ(int materialId, int warehouseId)
        {
            try
            {
                var eoq = await _eoqService.CalculateEOQAsync(materialId, warehouseId);
                return Ok(eoq);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("stockout-predictions")]
        public async Task<IActionResult> GetStockoutPredictions([FromQuery] int daysAhead = 14)
        {
            try
            {
                var predictions = await _stockoutService.PredictStockoutsAsync(daysAhead);
                return Ok(predictions);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("stockout-prediction/{materialId}/{warehouseId}")]
        public async Task<IActionResult> GetStockoutPrediction(int materialId, int warehouseId, [FromQuery] int daysAhead = 14)
        {
            try
            {
                var prediction = await _stockoutService.PredictStockoutAsync(materialId, warehouseId, daysAhead);
                if (prediction == null)
                {
                    return Ok(new { message = "Không có nguy cơ thiếu hàng trong thời gian dự đoán" });
                }
                return Ok(prediction);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}





























