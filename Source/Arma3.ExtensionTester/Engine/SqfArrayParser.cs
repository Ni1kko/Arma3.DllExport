using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Arma3.ExtensionTester.Engine
{
    public static class SqfArrayParser
    {
        private class NilValue { public override string ToString() => "nil"; }
        private static readonly NilValue Nil = new();

        public static string ToSqfString(object item)
        {
            return item switch
            {
                string s => "\"" + s.Replace("\"", "\"\"") + "\"",
                bool b => b.ToString().ToLower(),
                double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                List<object> list => "[" + string.Join(",", list.Select(ToSqfString)) + "]",
                _ => "nil"
            };
        }

        public static List<object> Parse(string input)
        {
            var tokenizer = new StringReader(input);
            return ParseArray(tokenizer);
        }

        private static void ConsumeWhitespace(StringReader reader)
        {
            while (char.IsWhiteSpace((char)reader.Peek())) reader.Read();
        }

        private static List<object> ParseArray(StringReader reader)
        {
            var list = new List<object>();
            ConsumeWhitespace(reader);
            if (reader.Read() != '[') throw new FormatException("Array must start with '['.");
            while (true)
            {
                ConsumeWhitespace(reader);
                if (reader.Peek() == ']') break;
                list.Add(ParseValue(reader));
                ConsumeWhitespace(reader);
                if (reader.Peek() == ',') reader.Read();
                else if (reader.Peek() != ']') throw new FormatException("Array elements must be separated by commas or end with ']'.");
            }
            reader.Read();
            return list;
        }

        private static object ParseValue(StringReader reader)
        {
            ConsumeWhitespace(reader);
            var peek = (char)reader.Peek();
            if (peek == '"') return ParseString(reader);
            if (peek == '[') return ParseArray(reader);
            return ParseLiteral(reader);
        }

        private static string ParseString(StringReader reader)
        {
            reader.Read();
            var sb = new StringBuilder();
            while (true)
            {
                var next = reader.Read();
                if (next == -1) throw new FormatException("Unterminated string literal.");
                var c = (char)next;
                if (c == '"')
                {
                    if ((char)reader.Peek() == '"') { sb.Append('"'); reader.Read(); }
                    else break;
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static object ParseLiteral(StringReader reader)
        {
            var sb = new StringBuilder();
            while (true)
            {
                var peek = reader.Peek();
                if (peek == -1 || char.IsWhiteSpace((char)peek) || (char)peek == ',' || (char)peek == ']') break;
                sb.Append((char)reader.Read());
            }

            var literal = sb.ToString();
            if (literal.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (literal.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            if (double.TryParse(literal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var number)) return number;

            return Nil;
        }
    }
}
