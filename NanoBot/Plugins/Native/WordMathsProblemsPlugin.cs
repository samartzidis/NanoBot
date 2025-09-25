using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Text.Json;
using System.Text.Json.Serialization;
using NanoBot.Configuration;
using Microsoft.Extensions.Options;

namespace NanoBot.Plugins.Native;

public class WordMathsProblem
{
    [JsonPropertyName("problemNumber")]
    public int ProblemNumber { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = string.Empty;

    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;

    [JsonPropertyName("calculation")]
    public string Calculation { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; }
}


public class WordMathsProblemsPlugin
{
    private readonly ILogger<WordMathsProblemsPlugin> _logger;
    private readonly List<WordMathsProblem> _problems;
    private readonly Random _random;
    private readonly HashSet<int> _usedProblems;

    public WordMathsProblemsPlugin(ILogger<WordMathsProblemsPlugin> logger, IOptions<AppConfig> appConfigOptions)
    {
        _logger = logger;
        _random = new Random();
        _problems = LoadProblems(appConfigOptions.Value);
        _usedProblems = new HashSet<int>();
    }

    private List<WordMathsProblem> LoadProblems(AppConfig appConfig)
    {
        try
        {
            // Check if JSON content is provided in configuration
            if (string.IsNullOrEmpty(appConfig.WordMathsProblemsJson))
            {
                _logger.LogWarning("WordMathsProblemsJson configuration is empty. Plugin will not function without problems data.");
                return new List<WordMathsProblem>();
            }

            _logger.LogInformation("Loading word maths problems from configuration");

            // Deserialize as array of problems
            var problems = JsonSerializer.Deserialize<List<WordMathsProblem>>(appConfig.WordMathsProblemsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (problems == null)
            {
                _logger.LogError("Failed to deserialize word maths problems data as array");
                return new List<WordMathsProblem>();
            }

            var totalProblems = problems.Count;
            var difficultyLevels = problems.Select(p => p.Difficulty).Distinct().Count();
            var categories = problems.Select(p => p.Category).Distinct().Count();
            
            _logger.LogInformation($"Loaded {totalProblems} problems across {difficultyLevels} difficulty levels and {categories} categories");
            return problems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading word maths problems data from configuration");
            return new List<WordMathsProblem>();
        }
    }


    [KernelFunction, Description("Gets a word maths problem from the specified category and difficulty level. If problemNumber is specified, returns that specific problem; otherwise returns a random problem.")]
    public WordMathsProblem GetWordMathsProblem(string category = null, string difficulty = null, int? problemNumber = null)
    {
        try
        {
            if (_problems.Count == 0)
            {
                _logger.LogWarning("No problems data available");
                return new WordMathsProblem
                {
                    ProblemNumber = 0,
                    Category = category,
                    Difficulty = difficulty,
                    Question = "No problems available",
                    Answer = "N/A",
                    Calculation = "N/A"
                };
            }

            // Filter problems based on criteria
            var filteredProblems = _problems.AsEnumerable();

            // Apply difficulty filter
            if (!string.IsNullOrEmpty(difficulty))
            {
                filteredProblems = filteredProblems.Where(p => p.Difficulty == difficulty);
            }

            // Apply category filter
            if (!string.IsNullOrEmpty(category))
            {
                filteredProblems = filteredProblems.Where(p => p.Category == category);
            }

            // Exclude already used problems (unless a specific problem number is requested)
            if (!problemNumber.HasValue)
            {
                filteredProblems = filteredProblems.Where(p => !_usedProblems.Contains(p.ProblemNumber));
            }

            var problemsList = filteredProblems.ToList();

            if (problemsList.Count == 0)
            {
                // Check if it's because all problems have been used
                var allProblemsForCriteria = _problems.AsEnumerable();
                if (!string.IsNullOrEmpty(difficulty))
                {
                    allProblemsForCriteria = allProblemsForCriteria.Where(p => p.Difficulty == difficulty);
                }
                if (!string.IsNullOrEmpty(category))
                {
                    allProblemsForCriteria = allProblemsForCriteria.Where(p => p.Category == category);
                }
                
                var totalAvailable = allProblemsForCriteria.Count();
                var usedCount = allProblemsForCriteria.Count(p => _usedProblems.Contains(p.ProblemNumber));
                
                if (usedCount > 0 && usedCount == totalAvailable)
                {
                    _logger.LogWarning($"All {totalAvailable} problems for category '{category}' and difficulty '{difficulty}' have been used. Consider calling ResetUsedProblems() to start over.");
                    return new WordMathsProblem
                    {
                        ProblemNumber = 0,
                        Category = category,
                        Difficulty = difficulty,
                        Question = $"All {totalAvailable} problems for this category and difficulty have been used. Call ResetUsedProblems() to start over.",
                        Answer = "N/A",
                        Calculation = "N/A"
                    };
                }
                else
                {
                    _logger.LogWarning($"No problems found for category '{category}' and difficulty '{difficulty}'");
                    return new WordMathsProblem
                    {
                        ProblemNumber = 0,
                        Category = category,
                        Difficulty = difficulty,
                        Question = "No problems available for the specified criteria",
                        Answer = "N/A",
                        Calculation = "N/A"
                    };
                }
            }

            WordMathsProblem selectedProblem;
            int actualProblemNumber = 0;

            if (problemNumber.HasValue)
            {
                // Get specific problem number
                if (problemNumber.Value >= 1 && problemNumber.Value <= problemsList.Count)
                {
                    selectedProblem = problemsList[problemNumber.Value - 1];
                    actualProblemNumber = problemNumber.Value;
                }
                else
                {
                    _logger.LogWarning($"Problem number {problemNumber.Value} out of range (1-{problemsList.Count}), returning random problem");
                    var randomIndex = _random.Next(problemsList.Count);
                    selectedProblem = problemsList[randomIndex];
                    actualProblemNumber = randomIndex + 1;
                }
            }
            else
            {
                // Return random problem
                var randomIndex = _random.Next(problemsList.Count);
                selectedProblem = problemsList[randomIndex];
                actualProblemNumber = randomIndex + 1;
            }

            _logger.LogDebug($"Selected {selectedProblem.Category} problem #{actualProblemNumber}: {selectedProblem.Question}");
            
            // Track this problem as used (only for random selections, not specific problem numbers)
            if (!problemNumber.HasValue)
            {
                _usedProblems.Add(selectedProblem.ProblemNumber);
                _logger.LogDebug($"Marked problem #{selectedProblem.ProblemNumber} as used");
            }
            
            return new WordMathsProblem
            {
                ProblemNumber = actualProblemNumber,
                Category = selectedProblem.Category,
                Difficulty = selectedProblem.Difficulty,
                Question = selectedProblem.Question,
                Answer = selectedProblem.Answer,
                Calculation = selectedProblem.Calculation,
                Explanation = selectedProblem.Explanation
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting word maths problem for category '{category}'");
            return new WordMathsProblem
            {
                ProblemNumber = 0,
                Category = category,
                Difficulty = difficulty,
                Question = "Error retrieving problem",
                Answer = "N/A",
                Calculation = "N/A"
            };
        }
    }

    [KernelFunction, Description("Gets all available problem categories for a specific difficulty level.")]
    public List<string> GetAvailableCategories(string difficulty = "3")
    {
        try
        {
            var categories = _problems
                .Where(p => p.Difficulty == difficulty)
                .Select(p => p.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            
            _logger.LogDebug($"Available categories for difficulty {difficulty}: {string.Join(", ", categories)}");
            return categories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available categories");
            return new List<string>();
        }
    }

    [KernelFunction, Description("Gets the count of problems in a specific category and difficulty level.")]
    public int GetProblemCount(string category, string difficulty = "3")
    {
        try
        {
            var count = _problems
                .Count(p => p.Category == category && p.Difficulty == difficulty);
            
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting problem count for category '{category}' and difficulty '{difficulty}'");
            return 0;
        }
    }

    [KernelFunction, Description("Gets all available difficulty levels.")]
    public List<string> GetAvailableDifficultyLevels()
    {
        try
        {
            var difficulties = _problems
                .Select(p => p.Difficulty)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
            
            _logger.LogDebug($"Available difficulty levels: {string.Join(", ", difficulties)}");
            return difficulties;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available difficulty levels");
            return new List<string>();
        }
    }

    [KernelFunction, Description("Resets the used problems tracking, allowing all problems to be selected again.")]
    public void ResetUsedProblems()
    {
        try
        {
            _usedProblems.Clear();
            _logger.LogInformation("Reset used problems tracking - all problems are now available again");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting used problems tracking");
        }
    }

}
