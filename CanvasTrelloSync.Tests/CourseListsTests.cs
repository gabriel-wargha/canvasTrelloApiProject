using CanvasTrelloSync.Services;

namespace CanvasTrelloSync.Tests;

public class CourseListsTests
{
    [Theory]
    [InlineData("PD-0141-ENHANCING-LEARNING-COMPUTER-SCIENCE-AND-MATHEMATICS", "PD-0141")]
    [InlineData("USF-PE-26", "USF-PE-26")]
    [InlineData("C1", "C1")]
    [InlineData("INTRO-COURSE", "INTRO-COURSE")]
    public void ShortCode_CourseCode_KeepsPartsUpToFirstNumber(string code, string expected)
    {
        Assert.Equal(expected, CourseLists.ShortCode(code));
    }

    [Fact]
    public void ShortCode_NoCode_ReturnsQuestionMark()
    {
        Assert.Equal("?", CourseLists.ShortCode(null));
    }

    [Theory]
    [InlineData("Prompt Engineering", "Prompt Engineering")]
    [InlineData("Code for Schools: Digitech Teacher Training", "Code for Schools: Digitech Teacher Training")]
    public void ListName_ShortName_KeepsItAsIs(string name, string expected)
    {
        Assert.Equal(expected, CourseLists.ListName(name, "CODE-1"));
    }

    [Fact]
    public void ListName_LongName_CutsAtWholeWordAndDropsDanglingAnd()
    {
        string name = "Enhancing Learning in Computer Science & Mathematics Using Cross Disciplinary Projects  ";

        Assert.Equal("Enhancing Learning in Computer Science…", CourseLists.ListName(name, "PD-0141-ENHANCING"));
    }

    [Fact]
    public void ListName_ExtraSpaces_CollapsesThem()
    {
        Assert.Equal("Prompt Engineering", CourseLists.ListName("  Prompt   Engineering ", "USF-PE-26"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void ListName_NoName_UsesShortCode(string? name)
    {
        Assert.Equal("PD-0141", CourseLists.ListName(name, "PD-0141-ENHANCING-LEARNING"));
    }
}
