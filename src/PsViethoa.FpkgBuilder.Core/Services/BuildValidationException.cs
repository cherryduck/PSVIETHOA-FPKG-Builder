using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Ném ra khi request không hợp lệ; chứa danh sách lỗi theo trường.</summary>
public sealed class BuildValidationException : Exception
{
    public BuildValidationException(IReadOnlyList<ValidationError> errors)
        : base(errors.Count == 0 ? Localization.Loc.T("Val.Invalid") : errors[0].Message)
    {
        Errors = errors;
    }

    public IReadOnlyList<ValidationError> Errors { get; }
}
