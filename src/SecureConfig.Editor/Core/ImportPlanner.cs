using System.Text;

namespace SecureConfig.Editor.Core;

/// <summary>Import (merge/replace) computation and change summary</summary>
public static class ImportPlanner
{
    /// <summary>
    /// Computes the import result. Does not modify current; returns a new document.
    /// </summary>
    /// <param name="current">Settings currently being edited</param>
    /// <param name="file">Contents of the JSON file to import</param>
    /// <param name="replace">
    /// true: replace everything with the file contents.
    /// false: merge — environments and variables in the file are added/overwritten; anything not in the file is kept.
    /// </param>
    public static ConfigDocument Apply(ConfigDocument current, ConfigDocument file, bool replace)
    {
        if (replace) return file.Clone();

        var result = current.Clone();
        foreach (var fe in file.Environments)
        {
            var re = result.FindEnvironment(fe.Name);
            if (re == null)
            {
                result.Environments.Add(fe.Clone());
                continue;
            }
            foreach (var fv in fe.Variables)
            {
                var rv = re.Find(fv.Name);
                if (rv != null) rv.Value = fv.Value;
                else re.Variables.Add(new ConfigVariable(fv.Name, fv.Value));
            }
        }
        return result;
    }

    /// <summary>Change summary text. changes = 0 if there are no changes.</summary>
    public static string Describe(ConfigDocument before, ConfigDocument after, out int changes)
    {
        var sb = new StringBuilder();
        changes = 0;

        foreach (var ae in after.Environments)
        {
            var be = before.FindEnvironment(ae.Name);
            if (be == null)
            {
                changes++;
                sb.AppendLine($"+ environment [{ae.Name}] added ({ae.Variables.Count} variables)");
                foreach (var v in ae.Variables) sb.AppendLine($"    + {v.Name}");
                continue;
            }

            var added = ae.Variables.Where(v => be.Find(v.Name) == null).Select(v => v.Name).ToList();
            var removed = be.Variables.Where(v => ae.Find(v.Name) == null).Select(v => v.Name).ToList();
            var changed = ae.Variables.Where(v => { var b = be.Find(v.Name); return b != null && b.Value != v.Value; })
                .Select(v => v.Name).ToList();

            if (added.Count + removed.Count + changed.Count == 0)
            {
                sb.AppendLine($"  environment [{ae.Name}] unchanged");
                continue;
            }
            changes += added.Count + removed.Count + changed.Count;
            sb.AppendLine($"~ environment [{ae.Name}]  added {added.Count} / changed {changed.Count} / removed {removed.Count}");
            foreach (var n in added) sb.AppendLine($"    + {n}");
            foreach (var n in changed) sb.AppendLine($"    ~ {n}");
            foreach (var n in removed) sb.AppendLine($"    - {n}");
        }

        foreach (var be in before.Environments.Where(b => after.FindEnvironment(b.Name) == null))
        {
            changes++;
            sb.AppendLine($"- environment [{be.Name}] removed ({be.Variables.Count} variables)");
        }
        return sb.ToString();
    }
}
