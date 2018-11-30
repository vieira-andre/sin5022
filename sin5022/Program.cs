using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace sin5022
{
    class Program
    {
        static void Main(string[] args)
        {
            var resultPath = ConfigurationManager.AppSettings["resultpath"];

            var provider = new CSharpCodeProvider();
            var parameters = new CompilerParameters(new[] { "mscorlib.dll", "System.Core.dll" }, resultPath, true)
            {
                GenerateExecutable = true
            };

            string sourceCode = File.ReadAllText(ConfigurationManager.AppSettings["sourcecode"]);

            string codeWithMethodInPlace = PromoteMethodPlacement(sourceCode);
            string codeWithMethodCallInPlace = PromoteMethodCall(codeWithMethodInPlace);
            string codeWithAssertionInPlace = PromoteAssertion(codeWithMethodCallInPlace);
            string codeToBeCompiled = PromoteCodeCoverage(codeWithAssertionInPlace);

            CompilerResults cr = provider.CompileAssemblyFromSource(parameters, codeToBeCompiled);

            if (cr.Errors.Count > 0)
            {
                Console.WriteLine("Errors building\n======\n\n{0}\n\n======\ninto\n\n{1}",
                    codeToBeCompiled, cr.PathToAssembly);
                foreach (CompilerError ce in cr.Errors)
                {
                    Console.WriteLine("  {0}", ce.ToString());
                    Console.WriteLine();
                }

                Console.ReadKey();
            }
            else
            {
                Console.WriteLine("Source\n======\n\n{0}\n\n======\nbuilt into {1} successfully.",
                    codeToBeCompiled, cr.PathToAssembly);

                Console.ReadKey();

                using (var process = Process.Start(resultPath))
                {
                    process.WaitForExit();
                }
            }
        }

        private static string PromoteMethodPlacement(string sourceCode)
        {
            var methodName = ConfigurationManager.AppSettings["methodname"];
            var matchMethodInSource = Regex.Match(sourceCode, @"(static)(\s)(int|string|double|object)(\s)(" + methodName + @")(\{*)[^}]*(\})");

            if (matchMethodInSource.Success)
            {
                string template = File.ReadAllText(ConfigurationManager.AppSettings["template"]);

                string instrumentedMethod = InstrumentMethod(matchMethodInSource.Value);
                string sourceWithInsertedMethod = Regex.Replace(template, @"(__methodPlacement__)", instrumentedMethod);

                return sourceWithInsertedMethod;
            }

            return sourceCode;
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
            var methodName = ConfigurationManager.AppSettings["methodname"];
            var requestArgs = File.ReadAllText(ConfigurationManager.AppSettings["request"]);
            var methodCall = string.Concat("var result = ", methodName, "(", requestArgs, ");");

            string codeWithMethodCallInPlace = Regex.Replace(codeWithMethodPlaced, @"(__methodCall__)", methodCall);

            return codeWithMethodCallInPlace;
        }

        private static string PromoteAssertion(string codeWithMethodCallInPlace)
        {
            var expectedValue = File.ReadAllText(ConfigurationManager.AppSettings["response"]);
            var assertionParams = File.ReadAllText(ConfigurationManager.AppSettings["assertion"]);

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
    }
}
