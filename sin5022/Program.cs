using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace sin5022
{
    class Program
    {
        static void Main(string[] args)
        {
            string resultPath = ConfigurationManager.AppSettings["resultpath"];

            var provider = new CSharpCodeProvider();
            var parameters = new CompilerParameters(new[] { "mscorlib.dll", "System.Core.dll" }, resultPath, true)
            {
                GenerateExecutable = true
            };

            string sourceCode = File.ReadAllText(ConfigurationManager.AppSettings["sourcecode"]);

            Tuple<bool, string> codeWithMethodInPlace = PromoteMethodPlacement(sourceCode);

            if (!codeWithMethodInPlace.Item1)
            {
                Console.WriteLine(codeWithMethodInPlace.Item2);
                _ = Console.ReadKey();

                Environment.Exit(-1);
            }

            string codeWithMethodCallInPlace = PromoteMethodCall(codeWithMethodInPlace.Item2);
            string codeWithAssertionInPlace = PromoteAssertion(codeWithMethodCallInPlace);
            string codeToBeCompiled = PromoteCodeCoverage(codeWithAssertionInPlace);

            ProceedToCompile(resultPath, provider, parameters, codeToBeCompiled);
        }

        private static Tuple<bool, string> PromoteMethodPlacement(string sourceCode)
        {
            string methodName = ConfigurationManager.AppSettings["methodname"];

            string desiredMethodFromSource = GetMethod(sourceCode, methodName);

            if (!string.IsNullOrEmpty(desiredMethodFromSource))
            {
                string template = File.ReadAllText(ConfigurationManager.AppSettings["template"]);

                string instrumentedMethod = InstrumentMethod(desiredMethodFromSource);
                string sourceWithInsertedMethod = Regex.Replace(template, @"(__methodPlacement__)", instrumentedMethod);

                return Tuple.Create(true, sourceWithInsertedMethod);
            }
            else
                return Tuple.Create(false, "[Error] The specified method could not be found.");
        }

        private static string GetMethod(string sourceCode, string methodName)
        {
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            SyntaxNode root = syntaxTree.GetRoot();

            MethodDeclarationSyntax method = root.DescendantNodes()
                             .OfType<MethodDeclarationSyntax>()
                             .Where(md => md.Identifier.ValueText.Equals(methodName))
                             .FirstOrDefault();

            if (method == null)
                return null;

            return method.ToString();
        }

        private static string InstrumentMethod(string uninstrumentedMethod)
        {
            string tempStr = uninstrumentedMethod.Replace(Environment.NewLine, $"ncount++;");
            tempStr = Regex.Replace(tempStr, @"(if|for|foreach|while)(\s*)((\()[^)]*(\)))(ncount\+\+;)", "$1$2$3");
            int idx = tempStr.IndexOf(@"ncount++;");
            tempStr = (idx < 0) ? tempStr : tempStr.Remove(idx, @"ncount++;".Length);
            int lastIdx = tempStr.LastIndexOf(@"ncount++;");
            tempStr = (idx < 0) ? tempStr : tempStr.Remove(lastIdx, @"ncount++;".Length);

            return tempStr;
        }

        private static string PromoteMethodCall(string codeWithMethodPlaced)
        {
            string methodName = ConfigurationManager.AppSettings["methodname"];
            string requestArgs = File.ReadAllText(ConfigurationManager.AppSettings["request"]);
            string methodCall = string.Concat("var result = ", methodName, "(", requestArgs, ");");

            string codeWithMethodCallInPlace = Regex.Replace(codeWithMethodPlaced, @"(__methodCall__)", methodCall);

            return codeWithMethodCallInPlace;
        }

        private static string PromoteAssertion(string codeWithMethodCallInPlace)
        {
            string expectedValue = File.ReadAllText(ConfigurationManager.AppSettings["response"]);
            string assertionParams = File.ReadAllText(ConfigurationManager.AppSettings["assertion"]);

            string[] assertParams = assertionParams.Split(new string[] { "<breakParam>" }, StringSplitOptions.None);

            string type = assertParams[0];
            var assertionCheck = assertParams[1];

            string expectedResult = string.Concat(type + " ", "expectedValue = ", expectedValue, ";");
            string codeWithExpectedResultInplace = Regex.Replace(codeWithMethodCallInPlace, @"(__expectedResult__)", expectedResult);

            string codeWithAssertionInPlace = Regex.Replace(codeWithExpectedResultInplace, @"(__assertionCheck__)", assertionCheck);

            return codeWithAssertionInPlace;
        }

        private static string PromoteCodeCoverage(string codeWithAssertionInPlace)
        {
            int ncountTotal = Regex.Matches(codeWithAssertionInPlace, @"ncount\+\+;").Count;

            string coverage = $"(Decimal.Divide(ncount, {ncountTotal}) * 100)";
            string codeCoverageStretch = string.Concat("Console.WriteLine(", "\"Code coverage (%): {0:0.0}\", ", coverage, ");");
            string codeWithCodeCoverageInPlace = Regex.Replace(codeWithAssertionInPlace, @"(__codeCoverage__)", codeCoverageStretch);

            return codeWithCodeCoverageInPlace;
        }

        private static void ProceedToCompile(string resultPath, CSharpCodeProvider provider, CompilerParameters parameters, string codeToBeCompiled)
        {
            CompilerResults cr = provider.CompileAssemblyFromSource(parameters, codeToBeCompiled);

            if (cr.Errors.Count > 0)
            {
                Console.WriteLine(
                    string.Concat("Errors building", "\n======\n\n", "{0}", "\n\n======\n", "into", "\n\n", "{1}"),
                    codeToBeCompiled, cr.PathToAssembly);

                foreach (CompilerError ce in cr.Errors)
                {
                    Console.WriteLine("  {0}", ce.ToString());
                    Console.WriteLine();
                }

                _ = Console.ReadKey();
            }
            else
            {
                Console.WriteLine(
                    string.Concat("Source", "\n======\n\n", "{0}", "\n\n======\n", "built into ", "{1}", " successfully."),
                    codeToBeCompiled, cr.PathToAssembly);

                _ = Console.ReadKey();

                using (Process process = Process.Start(resultPath))
                {
                    process.WaitForExit();
                }
            }
        }
    }
}
