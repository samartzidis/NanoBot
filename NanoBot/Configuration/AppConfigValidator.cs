//using Microsoft.Extensions.Options;
//using System.ComponentModel.DataAnnotations;

//namespace NanoBot.Configuration;

//public interface IAppConfigValidator
//{
//    ValidateOptionsResult Validate(string name, AppConfig options);
//}

//internal class AppConfigValidator : IValidateOptions<AppConfig>, IAppConfigValidator
//{
//    public ValidateOptionsResult Validate(string name, AppConfig options)
//    {
//        var results = new List<ValidationResult>();
//        var validator = new DataAnnotationsValidator.DataAnnotationsValidator();

//        if (!validator.TryValidateObjectRecursive(options, results))
//        {
//            var errors = results.Select(r => $"{r.ErrorMessage} ({string.Join(", ", r.MemberNames)})");
//            return ValidateOptionsResult.Fail(string.Join("; ", errors));
//        }

//        if (options?.TextToSpeechServiceProvider == TextToSpeechServiceProviderConfig.AzureSpeechService)
//        {
//            if (string.IsNullOrWhiteSpace(options.AzureSpeechServiceKey))
//                return ValidateOptionsResult.Fail($"Configuration {nameof(AppConfig.AzureSpeechServiceKey)} is required when using the {nameof(TextToSpeechServiceProviderConfig.AzureSpeechService)} provider.");

//            if (string.IsNullOrWhiteSpace(options.AzureSpeechServiceRegion))
//                return ValidateOptionsResult.Fail($"Configuration {nameof(AppConfig.AzureSpeechServiceRegion)} is required when using the {nameof(TextToSpeechServiceProviderConfig.AzureSpeechService)} provider.");
//        }

//        return ValidateOptionsResult.Success;
//    }
//}