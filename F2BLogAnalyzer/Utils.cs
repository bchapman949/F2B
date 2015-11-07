﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace F2B
{
    public class Utils
    {
        public static Tuple<IPAddress, int> ParseNetwork(string network)
        {
            IPAddress addr;
            int prefix;

            int pos = network.LastIndexOf('/');
            if (pos == -1)
            {
                addr = IPAddress.Parse(network).MapToIPv6();
                prefix = 128;
            }
            else
            {
                addr = IPAddress.Parse(network.Substring(0, pos));
                prefix = int.Parse(network.Substring(pos + 1));
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    prefix += 96;
                }
                addr = addr.MapToIPv6();
            }

            return new Tuple<IPAddress, int>(addr, prefix);
        }

        public static IPAddress GetNetwork(IPAddress addr, int prefix)
        {
            byte[] addrBytes = addr.GetAddressBytes();

            if (addrBytes.Length != 16)
                throw new ArgumentException("Only IPv6 (or IPv6 mapped IPv4 addresses) supported.");

            for (int i = (prefix + 7) / 8; i < 16; i++)
            {
                addrBytes[i] = 0;
            }

            if (prefix % 8 != 0)
            {
                addrBytes[prefix / 8] &= (byte)(0xff << (8 - (prefix % 8)));
            }

            return new IPAddress(addrBytes);
        }
    }



    public class SimpleExpression
    {
        public enum EvaluateTokenType
        {
            Number = 0x01,
            Unary = 0x02,
            Binary = 0x04,
            End = 0x08,
        }

        public static double Evaluate(string expr)
        {
            List<string> ops = new List<string>();
            List<double> vals = new List<double>();
            EvaluateTokenType allowed = EvaluateTokenType.Number | EvaluateTokenType.Unary;

            // normalize input expression
            expr = expr.ToLower();
            expr = expr.Replace(" ", "");
            expr = expr.Replace("true", "1");
            expr = expr.Replace("false", "0");

            for (int pos = 0; pos < expr.Length;)
            {
                string s1 = expr.Substring(pos, 1);
                string s2 = "\0\0";
                string s3 = "\0\0\0";
                string s4 = "\0\0\0\0";
                string s5 = "\0\0\0\0\0";

                if (pos < expr.Length - 1) s2 = expr.Substring(pos, 2);
                if (pos < expr.Length - 2) s3 = expr.Substring(pos, 3);
                if (pos < expr.Length - 3) s4 = expr.Substring(pos, 4);
                if (pos < expr.Length - 4) s5 = expr.Substring(pos, 5);

                if (s1.Equals("(") || s4.Equals("int(") || s5.Equals("bool("))
                {
                    if (!allowed.HasFlag(EvaluateTokenType.Number))
                    {
                        throw new ArgumentException("Invalid token type \"" + expr + "\"[" + pos + "]");
                    }

                    if (s1.Equals("(")) pos += 1;
                    else if (s4.Equals("int(")) pos += 4;
                    else if (s5.Equals("bool(")) pos += 5;

                    // recursively call Evaluate
                    int start = pos;
                    int bracketCount = 0;
                    for (; pos < expr.Length; pos++)
                    {
                        string s = expr.Substring(pos, 1);

                        if (s.Equals("("))
                        {
                            bracketCount++;
                        }
                        else if (s.Equals(")"))
                        {
                            if (bracketCount == 0)
                                break;

                            bracketCount--;
                        }
                    }

                    if (!(pos < expr.Length))
                    {
                        throw new ArgumentException("Invalid expression \"" + expr + "\"");
                    }

                    double val = Evaluate(expr.Substring(start, pos - start));

                    if (s1.Equals("(")) vals.Add(val);
                    else if (s4.Equals("int(")) vals.Add(((long)val));
                    else if (s5.Equals("bool(")) vals.Add(val == 0 ? 0 : 1);

                    pos += 1; // ")"

                    allowed = EvaluateTokenType.Binary | EvaluateTokenType.End;
                }
                else if (s2.Equals("==") || s2.Equals("!=")
                    || s2.Equals("<=") || s2.Equals(">=")
                    || s2.Equals("&&") || s2.Equals("||"))
                {
                    if (!allowed.HasFlag(EvaluateTokenType.Binary))
                    {
                        throw new ArgumentException("Invalid token type \"" + expr + "\"[" + pos + "]");
                    }

                    ops.Add(s2);
                    pos += 2;

                    allowed = EvaluateTokenType.Number | EvaluateTokenType.Unary;
                }
                else if (s1.Equals("+") || s1.Equals("-") || s1.Equals("*") || s1.Equals("/")
                    || s1.Equals("%") || s1.Equals(">") || s1.Equals("<")
                    || s1.Equals("&") || s1.Equals("^") || s1.Equals("|"))
                {
                    if (!allowed.HasFlag(EvaluateTokenType.Binary))
                    {
                        throw new ArgumentException("Invalid token type \"" + expr + "\"[" + pos + "]");
                    }

                    ops.Add(s1);
                    pos += 1;

                    allowed = EvaluateTokenType.Number | EvaluateTokenType.Unary;
                }
                else if (s1.Equals("!"))
                {
                    if (!allowed.HasFlag(EvaluateTokenType.Unary))
                    {
                        throw new ArgumentException("Invalid token type \"" + expr + "\"[" + pos + "]");
                    }

                    ops.Add(s1);
                    pos += 1;

                    allowed = EvaluateTokenType.Number;
                }
                else if (char.IsDigit(expr[pos]))
                {
                    if (!allowed.HasFlag(EvaluateTokenType.Number))
                    {
                        throw new ArgumentException("Invalid token type \"" + expr + "\"[" + pos + "]");
                    }

                    int start = pos;
                    while (pos < expr.Length && (char.IsDigit(expr, pos) || expr.Substring(pos, 1).Equals("."))) pos++;
                    vals.Add(double.Parse(expr.Substring(start, pos - start)));

                    allowed = EvaluateTokenType.Binary | EvaluateTokenType.End;
                }
                else
                {
                    throw new ArgumentException("Invalid character \"" + expr + "\"[" + pos + "]: s1=" + s1 + "s2=" + s2);
                }
            }

            if (!allowed.HasFlag(EvaluateTokenType.End))
            {
                throw new ArgumentException("Invalid token type \"" + expr + "\": non-terminating token at the end");
            }

            string[] operation_precedence = new string[] {
                "!", "*", "/", "%", "+", "-",
                "<", ">", "<=", ">=", "==", "!=",
                "&", "^", "|", "&&", "||"
            };

            foreach (string cop in operation_precedence)
            {
                int vpos = 0;
                foreach (string op in ops)
                {
                    if (!cop.Equals(op))
                    {
                        vpos++;
                        continue;
                    }

                    if (op.Equals("!"))
                        vals[vpos] = vals[vpos] == 0 ? 1 : 0;
                    else if (op.Equals("*"))
                        vals[vpos] *= vals[vpos + 1];
                    else if (op.Equals("/"))
                        vals[vpos] /= vals[vpos + 1];
                    else if (op.Equals("%"))
                        vals[vpos] = ((long)vals[vpos]) % ((long)vals[vpos + 1]);
                    else if (op.Equals("+"))
                        vals[vpos] += vals[vpos + 1];
                    else if (op.Equals("-"))
                        vals[vpos] -= vals[vpos + 1];
                    else if (op.Equals("<"))
                        vals[vpos] = vals[vpos] < vals[vpos + 1] ? 1 : 0;
                    else if (op.Equals(">"))
                        vals[vpos] = vals[vpos] > vals[vpos + 1] ? 1 : 0;
                    else if (op.Equals("<="))
                        vals[vpos] = vals[vpos] <= vals[vpos + 1] ? 1 : 0;
                    else if (op.Equals(">="))
                        vals[vpos] = vals[vpos] >= vals[vpos + 1] ? 1 : 0;
                    else if (op.Equals("=="))
                        vals[vpos] = vals[vpos] == vals[vpos + 1] ? 1 : 0;
                    else if (op.Equals("!="))
                        vals[vpos] = vals[vpos] != vals[vpos + 1] ? 1 : 0;
                    else if (op.Equals("&"))
                        vals[vpos] = ((long)vals[vpos]) & ((long)vals[vpos + 1]);
                    else if (op.Equals("^"))
                        vals[vpos] = ((long)vals[vpos]) ^ ((long)vals[vpos + 1]);
                    else if (op.Equals("|"))
                        vals[vpos] = ((long)vals[vpos]) | ((long)vals[vpos + 1]);
                    else if (op.Equals("&&"))
                        vals[vpos] = vals[vpos] != 0 && vals[vpos + 1] != 0 ? 1 : 0;
                    else if (op.Equals("||"))
                        vals[vpos] = vals[vpos] != 0 || vals[vpos + 1] != 0 ? 1 : 0;

                    // binary operators
                    if (!op.Equals("!"))
                        vals.RemoveAt(vpos + 1);
                }
                ops.RemoveAll(op => op.Equals(cop));
            }

            if (vals.Count != 1)
            {
                throw new ArgumentException("Invalid expression \"" + expr + "\": extra arguments");
            }

            return vals[0];
        }
    }



    class ProcessorEventStringTemplate
    {
        private IDictionary<string, string> repl;

        private static Dictionary<string, string> escapeMapping = new Dictionary<string, string>()
        {
            {Regex.Escape(@""""), "\""},
            {Regex.Escape(@"\\"), "\\\\"},
            {Regex.Escape(@"\a"), "\a"},
            {Regex.Escape(@"\b"), "\b"},
            {Regex.Escape(@"\f"), "\f"},
            {Regex.Escape(@"\n"), "\n"},
            {Regex.Escape(@"\r"), "\r"},
            {Regex.Escape(@"\t"), "\t"},
            {Regex.Escape(@"\v"), "\v"},
            {Regex.Escape(@"\0"), "\0"},
            {Regex.Escape(@"\${"), "${"},
        };
        private static Regex escapeRegex = new Regex(string.Join("|", escapeMapping.Keys));

        public ProcessorEventStringTemplate(EventEntry evtlog)
        {
            repl = new Dictionary<string, string>(20 + evtlog.ProcData.Count);

            // Environment
            repl["Environment.Now"] = DateTime.Now.Ticks.ToString();
            repl["Environment.DateTime"] = DateTime.Now.ToString();
            repl["Environment.MachineName"] = System.Environment.MachineName;

            // F2B Event
            repl["Event.Id"] = evtlog.Id.ToString();
            repl["Event.Timestamp"] = evtlog.Created.Ticks.ToString();
            repl["Event.Hostname"] = (evtlog.Hostname != null ? evtlog.Hostname : "");
            repl["Event.Type"] = evtlog.Input.InputType;
            repl["Event.Input"] = evtlog.Input.InputName;
            repl["Event.Selector"] = evtlog.Input.SelectorName;
            repl["Event.Address"] = evtlog.Address.ToString();
            repl["Event.Port"] = evtlog.Port.ToString();
            repl["Event.Username"] = (evtlog.Username != null ? evtlog.Username : "");
            repl["Event.Domain"] = (evtlog.Domain != null ? evtlog.Domain : "");
            repl["Event.Status"] = evtlog.Status.ToString();
            // Event
            if (evtlog.LogData.GetType() == typeof(EventRecordWrittenEventArgs)
                || evtlog.LogData.GetType().IsSubclassOf(typeof(EventRecordWrittenEventArgs)))
            {
                EventRecordWrittenEventArgs evtarg = evtlog.LogData as EventRecordWrittenEventArgs;
                EventRecord evtrec = evtarg.EventRecord;
                repl["Event.EventId"] = evtrec.Id.ToString();
                repl["Event.RecordId"] = evtrec.RecordId.ToString();
                repl["Event.MachineName"] = evtrec.MachineName;
                repl["Event.TimeCreated"] = evtrec.TimeCreated.Value.ToString();
                repl["Event.ProviderName"] = evtrec.ProviderName;
                repl["Event.ProcessId"] = evtrec.ProcessId.ToString();
            }
            else
            {
                repl["Event.EventId"] = "0";
                repl["Event.RecordId"] = "0";
                repl["Event.MachineName"] = "";
                repl["Event.TimeCreated"] = "0";
                repl["Event.ProviderName"] = "";
                repl["Event.ProcessId"] = "";
            }

            // Processor
            foreach (var item in evtlog.ProcData)
            {
                if (item.Value == null) repl[item.Key] = "";
                else repl[item.Key] = item.Value.ToString();
            }
        }

        public string ExpandTemplateVariables(string str)
        {
            StringBuilder output = new StringBuilder();

            // parse template line by line (report syntax error
            // in case of unmatched variable parenthesis)
            int pos;
            int start, end, par;
            bool subvar;
            string key;
            foreach (string line in str.Replace(Environment.NewLine, "\n").Split('\n'))
            {
                pos = 0;
                while (true)
                {
                    // try to find beginning of variable definition "${"
                    start = pos;
                    while (start < line.Length - 1 && (!(line[start] == '$' && line[start + 1] == '{') || (start > 0 && line[start - 1] == '\\'))) start++;
                    if (!(start < line.Length - 1))
                    {
                        output.Append(line.Substring(pos));
                        break;
                    }
                    output.Append(line.Substring(pos, start - pos));
                    pos = start;
                    start += 2;

                    // try to find end of variable definiton "}"
                    par = 0;
                    subvar = false;
                    end = start;
                    while (end < line.Length && (par > 0 || line[end] != '}'))
                    {
                        if (end < line.Length - 1 && line[end - 1] != '\\' && line[end] == '$' && line[end + 1] == '{')
                        {
                            par++;
                            subvar = true;
                        }
                        if (line[end] == '}')
                        {
                            par--;
                        }
                        end++;
                    }
                    if (!(end < line.Length))
                    {
                        Log.Warn("Unable to parse all variables in template line: " + line);
                        output.Append(line.Substring(pos));
                        break;
                    }
                    pos = end + 1;

                    // expand variable
                    if (subvar)
                    {
                        key = ExpandTemplateVariables(line.Substring(start, end - start));
                    }
                    else
                    {
                        key = line.Substring(start, end - start);
                    }

                    // parse default value from key
                    string defval = null;
                    if (key.Contains(":="))
                    {
                        int seppos = key.IndexOf(":=");
                        defval = key.Substring(seppos + 2);
                        key = key.Substring(0, seppos);
                    }

                    // replace variable
                    if (repl.ContainsKey(key))
                    {
                        output.Append(repl[key]);
                    }
                    else if (defval != null)
                    {
                        output.Append(defval);
                    }
                    else
                    {
                        output.Append("${");
                        output.Append(key);
                        output.Append("}");
                    }
                }

                output.Append(Environment.NewLine);
            }

            return output.ToString(0, output.Length - Environment.NewLine.Length);
        }

        public string EvalTemplateExpressions(string str)
        {
            StringBuilder output = new StringBuilder();

            // parse template line by line (report syntax error
            // in case of unmatched variable parenthesis)
            int pos;
            int start, end, par;
            bool subvar;
            string key;
            foreach (string line in str.Replace(Environment.NewLine, "\n").Split('\n'))
            {
                pos = 0;
                while (true)
                {
                    // try to find beginning of variable definition "$("
                    start = pos;
                    while (start < line.Length - 1 && (!(line[start] == '$' && line[start + 1] == '(') || (start > 0 && line[start - 1] == '\\'))) start++;
                    if (!(start < line.Length - 1))
                    {
                        output.Append(line.Substring(pos));
                        break;
                    }
                    output.Append(line.Substring(pos, start - pos));
                    pos = start;
                    start += 2;

                    // try to find end of variable definiton ")"
                    par = 0;
                    subvar = false;
                    end = start;
                    while (end < line.Length && (par > 0 || line[end] != ')'))
                    {
                        if (end < line.Length - 1 && line[end - 1] != '\\' && line[end] == '$' && line[end + 1] == '(')
                        {
                            subvar = true;
                        }

                        if (line[end] == '(') par++;
                        else if (line[end] == ')') par--;

                        end++;
                    }
                    if (!(end < line.Length))
                    {
                        Log.Warn("Unable to parse all variables in template line: " + line);
                        output.Append(line.Substring(pos));
                        break;
                    }
                    pos = end + 1;

                    // expand variable
                    if (subvar)
                    {
                        key = EvalTemplateExpressions(line.Substring(start, end - start));
                    }
                    else
                    {
                        key = line.Substring(start, end - start);
                    }

                    // parse default value from key
                    string defval = null;
                    if (key.Contains(":="))
                    {
                        int seppos = key.IndexOf(":=");
                        defval = key.Substring(seppos + 2);
                        key = key.Substring(0, seppos);
                    }

                    // replace variable
                    try
                    {
                        output.Append(SimpleExpression.Evaluate(key));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Unable to evaluate expression \"" + key + "\": " + ex.Message);

                        if (defval != null)
                        {
                            output.Append(defval);
                        }
                        else
                        {
                            output.Append("$(");
                            output.Append(key);
                            output.Append(")");
                        }
                    }
                }

                output.Append(Environment.NewLine);
            }

            return output.ToString(0, output.Length - Environment.NewLine.Length);
        }

        public static string UnEscape(string s)
        {
            return escapeRegex.Replace(s, UnEscapeMatchEval);
        }

        public string Apply(string str)
        {
            string tmp = ExpandTemplateVariables(str);
            tmp = EvalTemplateExpressions(tmp);
            return UnEscape(tmp);
        }

        private static string UnEscapeMatchEval(Match m)
        {
            if (escapeMapping.ContainsKey(m.Value))
            {
                return escapeMapping[m.Value];
            }
            return escapeMapping[Regex.Escape(m.Value)];
        }
    }
}
