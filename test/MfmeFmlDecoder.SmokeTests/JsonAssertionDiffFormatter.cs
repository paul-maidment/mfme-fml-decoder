using System;
using System.Collections.Generic;
using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace MfmeFmlDecoder.SmokeTests
{
    /// <summary>
    /// Builds a unified line diff between two JSON strings for assertion messages.
    /// Uses ANSI colours when supported (see <see cref="ShouldUseAnsiColor"/>).
    /// </summary>
    internal static class JsonAssertionDiffFormatter
    {
        private const string Reset = "\u001b[0m";
        private const string Red = "\u001b[31m";
        private const string Green = "\u001b[32m";
        private const string Yellow = "\u001b[33m";
        private const string Dim = "\u001b[90m";

        /// <summary>
        /// Color is off when <c>NO_COLOR</c> is set (see https://no-color.org/).
        /// On Windows, ANSI requires a modern terminal (Windows Terminal, VS Code, etc.).
        /// </summary>
        internal static bool ShouldUseAnsiColor()
        {
            string noColor = Environment.GetEnvironmentVariable("NO_COLOR");
            if (!string.IsNullOrEmpty(noColor))
            {
                return false;
            }

            return true;
        }

        public static string FormatUnifiedDiff(string expectedText, string actualText)
        {
            bool useColor = ShouldUseAnsiColor();
            DiffPaneModel diff = InlineDiffBuilder.Diff(expectedText, actualText);
            var sb = new StringBuilder();
            sb.AppendLine("Unified diff (- expected file, + stdout). Unchanged lines are dimmed when color is on:");
            foreach (DiffPiece line in diff.Lines)
            {
                AppendLine(sb, line, useColor);
            }

            if (useColor)
            {
                sb.Append(Reset);
            }

            return sb.ToString();
        }

        private static void AppendLine(StringBuilder sb, DiffPiece line, bool useColor)
        {
            switch (line.Type)
            {
                case ChangeType.Deleted:
                    WritePrefixedLine(sb, "- ", line.Text, useColor ? Red : null);
                    return;
                case ChangeType.Inserted:
                    WritePrefixedLine(sb, "+ ", line.Text, useColor ? Green : null);
                    return;
                case ChangeType.Modified:
                    AppendModifiedLine(sb, line, useColor);
                    return;
                case ChangeType.Imaginary:
                    WritePrefixedLine(sb, "  ", line.Text, useColor ? Dim : null);
                    return;
                default:
                    WritePrefixedLine(sb, "  ", line.Text, useColor ? Dim : null);
                    return;
            }
        }

        private static void AppendModifiedLine(StringBuilder sb, DiffPiece line, bool useColor)
        {
            IList<DiffPiece> parts = line.SubPieces;
            if (!useColor || parts == null || parts.Count == 0)
            {
                WritePrefixedLine(sb, "~ ", line.Text, useColor ? Yellow : null);
                return;
            }

            sb.Append(useColor ? Yellow : string.Empty).Append("~ ").Append(Reset);
            foreach (DiffPiece piece in parts)
            {
                string segment = piece.Text ?? string.Empty;
                if (!useColor)
                {
                    sb.Append(segment);
                    continue;
                }

                switch (piece.Type)
                {
                    case ChangeType.Deleted:
                        sb.Append(Red).Append(segment).Append(Reset);
                        break;
                    case ChangeType.Inserted:
                        sb.Append(Green).Append(segment).Append(Reset);
                        break;
                    default:
                        sb.Append(Dim).Append(segment).Append(Reset);
                        break;
                }
            }

            sb.AppendLine();
        }

        private static void WritePrefixedLine(StringBuilder sb, string prefix, string text, string ansiColorOrNull)
        {
            text ??= string.Empty;
            if (ansiColorOrNull != null)
            {
                sb.Append(ansiColorOrNull).Append(prefix).Append(text).AppendLine(Reset);
            }
            else
            {
                sb.Append(prefix).AppendLine(text);
            }
        }
    }
}
