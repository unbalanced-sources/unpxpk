using CommandLine;

namespace unpxpk{
     public class Options
    {
        [Option('h',"header", Default = "none",
        HelpText = "(default: \"none\") Type of header added to file. Could be \"none\" or \"picture\""
        )]
        public string header { get; set; }

        [Option('f',"infile", Required = true, HelpText = "Path + file name to be unpack" ) ]
        public string inFileName { get; set; }

        // [Option("outfile", HelpText = "Directory and model name to put processed file. Not required. If skipped - initial will be used." ) ]
        // public string outModelName { get; set; }
    }
}