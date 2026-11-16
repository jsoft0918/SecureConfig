using System.Text;

namespace SecureConfig.Editor.Core;

/// <summary>Writes settings JSON (reading is done by SecureConfig.ConfigDocument.Parse)</summary>
public static class ConfigJson
{
    /// <summary>
    /// Serializes the document to a JSON string, preserving the order of environments and variables.
    /// </summary>
    /// <param name="doc">Settings document</param>
    /// <param name="indented">true: human-readable indentation (for export); false: single line (for encryption and change detection)</param>
    public static string Write(ConfigDocument doc, bool indented)
    {
        var sb = new StringBuilder();
        string nl = indented ? "\r\n" : "";
        string i1 = indented ? "  " : "";
        string i2 = indented ? "    " : "";
        string colon = indented ? ": " : ":";

        sb.Append('{');
        for (int e = 0; e < doc.Environments.Count; e++)
        {
            var env = doc.Environments[e];
            sb.Append(nl).Append(i1).Append(Quote(env.Name)).Append(colon).Append('{');
            for (int v = 0; v < env.Variables.Count; v++)
            {
                var item = env.Variables[v];
                sb.Append(nl).Append(i2).Append(Quote(item.Name)).Append(colon).Append(Quote(item.Value));
                if (v < env.Variables.Count - 1) sb.Append(',');
            }
            if (env.Variables.Count > 0) sb.Append(nl).Append(i1);
            sb.Append('}');
            if (e < doc.Environments.Count - 1) sb.Append(',');
        }
        if (doc.Environments.Count > 0) sb.Append(nl);
        sb.Append('}').Append(nl);
        return sb.ToString();
    }

    private static string Quote(string s)
    {
        s ??= "";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
