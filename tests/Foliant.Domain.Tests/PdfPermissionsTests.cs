using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Domain.Tests;

/// <summary>
/// Закрепляет битовое представление и комбинации <see cref="PdfPermissions"/>.
/// Биты НЕ совпадают с битами PDF P-entry — это намеренно (домен моделирует
/// намерение, реализация делает маппинг). Тесты ловят случайные сдвиги enum-значений,
/// которые сломали бы сериализацию/golden-file совместимость.
/// </summary>
public sealed class PdfPermissionsTests
{
    [Theory]
    [InlineData(PdfPermissions.None, 0)]
    [InlineData(PdfPermissions.Print, 1)]
    [InlineData(PdfPermissions.Modify, 2)]
    [InlineData(PdfPermissions.Copy, 4)]
    [InlineData(PdfPermissions.Annotate, 8)]
    [InlineData(PdfPermissions.FillForms, 16)]
    [InlineData(PdfPermissions.Accessibility, 32)]
    [InlineData(PdfPermissions.Assemble, 64)]
    [InlineData(PdfPermissions.HighQualityPrint, 128)]
    public void IndividualFlags_HaveStableBitValues(PdfPermissions flag, int expected)
    {
        ((int)flag).Should().Be(expected);
    }

    [Fact]
    public void All_EqualsBitwiseOrOfEveryIndividualFlag()
    {
        PdfPermissions composite = PdfPermissions.Print
            | PdfPermissions.Modify
            | PdfPermissions.Copy
            | PdfPermissions.Annotate
            | PdfPermissions.FillForms
            | PdfPermissions.Accessibility
            | PdfPermissions.Assemble
            | PdfPermissions.HighQualityPrint;

        PdfPermissions.All.Should().Be(composite);
        ((int)PdfPermissions.All).Should().Be(0xFF);
    }

    [Fact]
    public void All_ContainsEveryIndividualFlag()
    {
        // HasFlag — это контракт, на который положится UI (чекбоксы) и реализация-маппинг.
        PdfPermissions.All.HasFlag(PdfPermissions.Print).Should().BeTrue();
        PdfPermissions.All.HasFlag(PdfPermissions.HighQualityPrint).Should().BeTrue();
        PdfPermissions.All.HasFlag(PdfPermissions.Accessibility).Should().BeTrue();
    }

    [Fact]
    public void None_HasNoFlags()
    {
        PdfPermissions.None.HasFlag(PdfPermissions.Print).Should().BeFalse();
        PdfPermissions.None.HasFlag(PdfPermissions.Modify).Should().BeFalse();
        ((int)PdfPermissions.None).Should().Be(0);
    }

    [Fact]
    public void Composition_PrintOnly_IsDistinctFromAllOthers()
    {
        // Read-only-with-print кейс юристов: запрет копирования/модификации, печать разрешена.
        PdfPermissions readOnlyPrint = PdfPermissions.Print | PdfPermissions.HighQualityPrint;

        readOnlyPrint.HasFlag(PdfPermissions.Print).Should().BeTrue();
        readOnlyPrint.HasFlag(PdfPermissions.Copy).Should().BeFalse();
        readOnlyPrint.HasFlag(PdfPermissions.Modify).Should().BeFalse();
        readOnlyPrint.Should().NotBe(PdfPermissions.All);
        readOnlyPrint.Should().NotBe(PdfPermissions.None);
    }

    [Fact]
    public void FlagsAttribute_IsApplied()
    {
        // [Flags] меняет ToString() на «A, B, C» вместо «<unknown>». Закрепляем — оно нам нужно
        // для логов/диагностики и для CA1069-style проверок аналайзеров.
        typeof(PdfPermissions).GetCustomAttributes(typeof(System.FlagsAttribute), inherit: false)
            .Should().NotBeEmpty();
    }
}
