namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 9 (22 Oct 2026): the mojibake repair in PastedTable needs
/// Windows-1252 without the code-pages provider being registered first -
/// these characters (the euro, trade-mark and per-mille signs a mis-read
/// curly apostrophe or "É" becomes) have no Latin-1 byte.
/// </summary>
public class BugHunt9_PastedTable_csTests
{
    [Fact]
    public void A_misread_curly_apostrophe_is_put_back()
    {
        // "O’Brien" (E2 80 99) read as Windows-1252 is "Oâ€™Brien".
        Assert.Equal("<td>O’Brien</td>", PastedTable.RepairUtf8("<td>Oâ€™Brien</td>"));
    }

    [Fact]
    public void A_misread_capital_E_acute_is_put_back_alongside_other_names()
    {
        // "Éric" (C3 89) read as Windows-1252 is "Ã‰ric".
        var html = "<td>Ã‰ric Dubois</td><td>JosÃ© PÃ©rez</td>";
        Assert.Equal("<td>Éric Dubois</td><td>José Pérez</td>", PastedTable.RepairUtf8(html));
    }

    [Fact]
    public void A_pasted_table_with_misread_names_reads_them_clean()
    {
        var table = PastedTable.Read(
            "<table><tr><th>Driver</th><th>Transponder</th></tr>"
            + "<tr><td>Sean Oâ€™Brien</td><td>1234567</td></tr></table>",
            null);

        Assert.NotNull(table);
        Assert.Equal("Sean O’Brien", table!.Rows[0][0]);
    }
}
