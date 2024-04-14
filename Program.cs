string[] PARAMS = ["-a", "-app", "-application"];
string? PROCESS = null;

if (args.Length != 2)
    Console.Error.WriteLine("Invalid number of arguments");
else {
    if (PARAMS.Contains(args[0]))
        PROCESS = args[1];

    if (null != PROCESS)
        Console.WriteLine(string.Format("parameter={0}", PROCESS));
    else {
        Console.Error.WriteLine("No valid arguments");
        Environment.Exit(0);
    }
}