using System;
using System.Text.Json;

namespace Modules.Approvals.Services
{
    public interface IRiskAnalyzer
    {
        string Analyze(string actionType, string payloadJson);
    }

    public class RiskAnalyzer : IRiskAnalyzer
    {
        public string Analyze(string actionType, string payloadJson)
        {
            if (string.Equals(actionType, "CRMUpdate", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var doc = JsonDocument.Parse(payloadJson);
                    var root = doc.RootElement;

                    // If payload updates budget
                    if (root.TryGetProperty("budget", out var budgetProp) || root.TryGetProperty("Budget", out budgetProp))
                    {
                        if (budgetProp.ValueKind == JsonValueKind.Number)
                        {
                            var budget = budgetProp.GetDecimal();
                            if (budget > 1000000)
                            {
                                return "High";
                            }
                        }
                    }
                }
                catch
                {
                    // Safe guard: treat invalid payloads as High risk
                    return "High";
                }
            }
            else if (string.Equals(actionType, "SendDiscount", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var doc = JsonDocument.Parse(payloadJson);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("discount", out var discountProp) || root.TryGetProperty("Discount", out discountProp))
                    {
                        if (discountProp.ValueKind == JsonValueKind.Number)
                        {
                            var discount = discountProp.GetDecimal();
                            if (discount > 20)
                            {
                                return "High";
                            }
                        }
                    }
                }
                catch
                {
                    return "High";
                }
            }
            else if (string.Equals(actionType, "DeleteCustomer", StringComparison.OrdinalIgnoreCase))
            {
                return "High";
            }

            return "Low";
        }
    }
}
