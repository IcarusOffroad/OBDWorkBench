using System;
using System.Globalization;

namespace OBDWorkbench.Services;

/// <summary>
/// Small deterministic arithmetic evaluator used for user-created calculated values.
/// Supports A/B/C/D, numeric literals, parentheses, and + - * / only.
/// </summary>
public static class CalculatedExpression
{
    public static bool TryEvaluate(string? expression, double a, double b, double c, double d, out double value, out string error)
    {
        value = 0;
        error = "";
        try
        {
            var parser = new Parser(expression ?? "", a, b, c, d);
            value = parser.Parse();
            if (!double.IsFinite(value))
                throw new InvalidOperationException("Formula result is not finite.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            value = 0;
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly double _a, _b, _c, _d;
        private int _pos;

        public Parser(string text, double a, double b, double c, double d)
        {
            _text = text;
            _a = a; _b = b; _c = c; _d = d;
        }

        public double Parse()
        {
            Skip();
            if (_text.Length == 0) throw new FormatException("Enter a formula.");
            double result = ParseExpression();
            Skip();
            if (_pos != _text.Length) throw new FormatException($"Unexpected character '{_text[_pos]}' at position {_pos + 1}.");
            return result;
        }

        private double ParseExpression()
        {
            double value = ParseTerm();
            while (true)
            {
                Skip();
                if (Take('+')) value += ParseTerm();
                else if (Take('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            double value = ParseFactor();
            while (true)
            {
                Skip();
                if (Take('*')) value *= ParseFactor();
                else if (Take('/'))
                {
                    double divisor = ParseFactor();
                    if (Math.Abs(divisor) < 1e-15) throw new DivideByZeroException("Formula attempted to divide by zero.");
                    value /= divisor;
                }
                else return value;
            }
        }

        private double ParseFactor()
        {
            Skip();
            if (Take('+')) return ParseFactor();
            if (Take('-')) return -ParseFactor();
            if (Take('('))
            {
                double value = ParseExpression();
                Skip();
                if (!Take(')')) throw new FormatException("Missing closing parenthesis.");
                return value;
            }

            if (_pos < _text.Length)
            {
                char ch = char.ToUpperInvariant(_text[_pos]);
                if (ch is 'A' or 'B' or 'C' or 'D')
                {
                    _pos++;
                    return ch switch { 'A' => _a, 'B' => _b, 'C' => _c, _ => _d };
                }
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            Skip();
            int start = _pos;
            bool seenDot = false;
            bool seenExp = false;
            while (_pos < _text.Length)
            {
                char ch = _text[_pos];
                if (char.IsDigit(ch)) { _pos++; continue; }
                if (ch == '.' && !seenDot && !seenExp) { seenDot = true; _pos++; continue; }
                if ((ch == 'e' || ch == 'E') && !seenExp && _pos > start)
                {
                    seenExp = true; _pos++;
                    if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                    continue;
                }
                break;
            }
            if (_pos == start || !double.TryParse(_text[start.._pos], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException($"Expected A, B, C, D, a number, or '(' at position {start + 1}.");
            return value;
        }

        private void Skip() { while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++; }
        private bool Take(char ch)
        {
            if (_pos < _text.Length && _text[_pos] == ch) { _pos++; return true; }
            return false;
        }
    }
}
