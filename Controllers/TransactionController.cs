using fourtitudeAsia.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Security.Cryptography;
using log4net;
using Newtonsoft.Json;

namespace fourtitudeAsia.Controllers
{
    [Route("api")]
    [ApiController]
    public class TransactionController : ControllerBase
    {

        private static readonly Dictionary<string , (string PartnerKey, string PartnerPassword)> AllowedPartners =
             new()
             {
                { "FG-00001", ("FAKEGOOGLE", "FAKEPASSWORD1234") },
                { "FG-00002", ("FAKEPEOPLE", "FAKEPASSWORD4578") }
             };

        private static readonly ILog logger = LogManager.GetLogger(typeof(TransactionController));
        private static string EncryptBase64PasswordForLog(string base64Password)
        {
            if (string.IsNullOrWhiteSpace(base64Password))
                return string.Empty;

            // AES key & IV (store securely in config, not hardcoded in production)
            string key = "1234567890123456"; // 16 chars = 128-bit key
            string iv = "abcdefghijklmnop"; // 16 chars = 128-bit IV

            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.IV = Encoding.UTF8.GetBytes(iv);

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

            // Decode Base64 → encrypt → return Base64-encrypted text
            byte[] decodedPasswordBytes = Convert.FromBase64String(base64Password);
            byte[] encryptedBytes = encryptor.TransformFinalBlock(decodedPasswordBytes, 0, decodedPasswordBytes.Length);

            return Convert.ToBase64String(encryptedBytes);
        }

        [HttpPost("submittrxmessage")]
        public IActionResult SubmitTransaction([FromBody] TransactionRequest request)
        {
            string safePassword = EncryptBase64PasswordForLog(request.PartnerPassword);
            // 🔹 Create a copy of the request for logging only
            var requestForLog = new TransactionRequest
            {
                PartnerKey = request.PartnerKey,
                PartnerRefNo = request.PartnerRefNo,
                PartnerPassword = safePassword,
                TotalAmount = request.TotalAmount,
                Timestamp = request.Timestamp,
                Sig = request.Sig,
                Items = request.Items
            };
            logger.Info("\nIncoming SubmitTransaction request: " + JsonConvert.SerializeObject(requestForLog));
            var missingFields = new List<string>();

            if (request == null)
            {
                return BadRequest(new TransactionResponse
                {
                    Result = 0,
                    ResultMessage = "Request body is null."
                });
            }

            if (string.IsNullOrWhiteSpace(request.PartnerKey))
                missingFields.Add(nameof(request.PartnerKey));

            if (string.IsNullOrWhiteSpace(request.PartnerRefNo))
                missingFields.Add(nameof(request.PartnerRefNo));

            if (string.IsNullOrWhiteSpace(request.PartnerPassword))
                missingFields.Add(nameof(request.PartnerPassword));

            if (request.TotalAmount <= 0)
                missingFields.Add(nameof(request.TotalAmount));

            if (string.IsNullOrWhiteSpace(request.Timestamp))
                missingFields.Add(nameof(request.Timestamp));

            if (string.IsNullOrWhiteSpace(request.Sig))
                missingFields.Add(nameof(request.Sig));

            if (missingFields.Any())
            {
                logger.Warn($"Missing required fields: {string.Join(", ", missingFields)}");
                return BadRequest(new TransactionResponse
                {
                    Result = 0,
                    ResultMessage =  string.Join(", ", missingFields) + " is required." 
                });
            }


            // Partner validation
            if (!AllowedPartners.TryGetValue(request.PartnerRefNo, out var partnerInfo) ||
                     partnerInfo.PartnerKey != request.PartnerKey)
            {
                logger.Warn($"Unauthorized access attempt. PartnerRefNo: {request.PartnerRefNo}");
                return Unauthorized(new TransactionResponse
                {
                    Result = 0,
                    ResultMessage = "Access Denied!"
                });
            }

            // Validate Base64 password
            var expectedBase64Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(partnerInfo.PartnerPassword));
            if (request.PartnerPassword != expectedBase64Password)
            {
                logger.Warn("Password mismatch for PartnerRefNo: " + request.PartnerRefNo);
                return Unauthorized(new TransactionResponse
                {
                    Result = 0,
                    ResultMessage = "Access Denied!"
                });
            }

            // Validate timestamp format
            if (!DateTime.TryParse(request.Timestamp, out var _))
            {
                logger.Warn("Invalid timestamp format: " + request.Timestamp);
                return BadRequest(new TransactionResponse
                {
                    Result = 0,
                    ResultMessage = "Invalid timestamp format."
                });
            }

            // Items validation (if provided)
            if (request.Items != null)
            {
                foreach (var item in request.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.PartnerItemRef) ||
                        string.IsNullOrWhiteSpace(item.Name) ||
                        item.Qty <= 0 || item.Qty > 5 ||
                        item.UnitPrice <= 0)
                    {
                        logger.Warn("Invalid item details found: " + JsonConvert.SerializeObject(item));
                        return BadRequest(new TransactionResponse
                        {
                            Result = 0,
                            ResultMessage = "Invalid item details."
                        });
                    }
                }
              long calculatedTotal = request.Items.Sum(item => item.Qty * item.UnitPrice);
                if (request.TotalAmount != calculatedTotal)
                {
                    logger.Warn($"Total amount mismatch. Expected: {calculatedTotal}, Provided: {request.TotalAmount}");
                    return BadRequest(new TransactionResponse
                    {
                        Result = 0,
                        ResultMessage = "Invalid Total Amount. "
                    });
                }


            }


            // Signature validation
            var sigTimestamp = DateTime.Parse(request.Timestamp).ToString("yyyyMMddHHmmss");
            var concatStr = $"{sigTimestamp}{request.PartnerKey}{request.PartnerRefNo}{request.TotalAmount}{request.PartnerPassword}";
            var computedSig = GenerateSignature(concatStr);
            if (computedSig != request.Sig)
            {
                logger.Warn($"Signature mismatch. Computed: {computedSig}, Provided: {request.Sig}");
                return Unauthorized(new TransactionResponse
                {
                    Result = 0,
                    ResultMessage = $"Access Denied! "
                });
            }

            //check for expired
            DateTime serverTime = DateTime.UtcNow;
            DateTime requestTime;

            if (!DateTime.TryParse(request.Timestamp, out requestTime))
            {
                logger.Warn($"Invalid timestamp format : {request.Timestamp}");

                return BadRequest(new TransactionResponse
                {
                    Result = 0,
                    ResultMessage = "Invalid timestamp format."
                });
            }
            // Convert UTC request time to local server time
            requestTime = requestTime.ToUniversalTime();

            // Check ±5 minutes in UTC
            if (requestTime < serverTime.AddMinutes(-5) || requestTime > serverTime.AddMinutes(5))
            {
                Console.WriteLine("Server UTC Time: " + serverTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
                Console.WriteLine("Provided Time: " + requestTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
                logger.Warn($"Timestamp validation failed. " +
                 $"Server UTC Time: {serverTime:yyyy-MM-ddTHH:mm:ss.fffffffZ}, " +
                 $"Provided Time: {requestTime:yyyy-MM-ddTHH:mm:ss.fffffffZ}, " +
                 $"Allowed Range: ±5 minutes.");

                return BadRequest(new TransactionResponse
                {
                    Result = 0,
                    ResultMessage = $"Expired. Provided timestamp exceed server time ±5min"
                });
            }

            var response = new TransactionResponse
            {
                Result = 1,
                TotalAmount = request.TotalAmount,
                TotalDiscount = CalculateDiscount(request.TotalAmount),
                FinalAmount = request.TotalAmount - CalculateDiscount(request.TotalAmount)
            };

            logger.Info("Transaction processed successfully. Response: " + JsonConvert.SerializeObject(response));

            // ✅ If all validations pass
            return Ok(new TransactionResponse
            {
                Result = 1,
                TotalAmount = request.TotalAmount,
                TotalDiscount = CalculateDiscount(request.TotalAmount),
                FinalAmount = request.TotalAmount - CalculateDiscount(request.TotalAmount), // amount after discount
            });
        }

        private static long CalculateDiscount(long totalAmountCents)
        {
            // Convert cents to MYR (integer part)
            long totalAmountMYR = totalAmountCents / 100;

            double baseDiscount = 0;
            double extraDiscount = 0;

            // ✅ Base Discount
            if (totalAmountMYR >= 200 && totalAmountMYR <= 500)
                baseDiscount = 0.05;
            else if (totalAmountMYR >= 501 && totalAmountMYR <= 800)
                baseDiscount = 0.07;
            else if (totalAmountMYR >= 801 && totalAmountMYR <= 1200)
                baseDiscount = 0.10;
            else if (totalAmountMYR > 1200)
                baseDiscount = 0.15;

            // ✅ Conditional Discount: Prime check
            if (totalAmountMYR > 500 && IsPrime(totalAmountMYR))
                extraDiscount += 0.08;

            // ✅ Conditional Discount: Ends with 5 (MYR)
            if (totalAmountMYR > 900 && totalAmountMYR % 10 == 5)
                extraDiscount += 0.10;

            // ✅ Cap total discount at 20%
            double totalDiscount = baseDiscount + extraDiscount;
            if (totalDiscount > 0.20)
                totalDiscount = 0.20;

            // ✅ Return discount amount in cents
            return (long)(totalAmountCents * totalDiscount);
        }

        // Helper method: Prime number check
        private static bool IsPrime(long number)
        {
            if (number < 2) return false; // 2,3,5,7,9 ,  start at 2 is a must
            if (number % 2 == 0 && number != 2) return false;

            for (long i = 3; i * i <= number; i += 2) //skip even 
                if (number % i == 0) return false;

            return true;
        }

        private static string GenerateSignature(string input)
        {
            // SHA256 UTF-8 lowercase hex → Base64
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            var hexString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(hexString));
        }
     
    }
}
