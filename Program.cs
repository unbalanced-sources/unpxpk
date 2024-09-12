using CommandLine;
using unpxpk;
// See https://aka.ms/new-console-template for more information

const int MIN_REPEAT_LENGTH = 3;
const int PRE_BUFFER_LENGTH =  4;
const int MAGIC_CONST = MIN_REPEAT_LENGTH + 0xF + PRE_BUFFER_LENGTH;


var _args = args;

if (_args.Count() == 0)
{
    _args = new string[]{"--help"};
} 

#if DEBUG
Parser.Default.ParseArguments<Options>(_args[0].Split()) //change to "_args" if release
                .WithParsed(UnpackFile)
                .WithNotParsed(HandleParseError);
#else
Parser.Default.ParseArguments<Options>(_args) //change to "_args" if release
                .WithParsed(UnpackFile)
                .WithNotParsed(HandleParseError);
#endif


static void HandleParseError(IEnumerable<Error> errs)
{
        if (errs.IsVersion())
    {
            Console.WriteLine("Version Request");
        return;
    }
    if (errs.IsHelp())
    {
            Console.WriteLine("Help Request");
        return;
    }
}


void UnpackFile(Options options){
    
    string _inFileName = options.inFileName;
    string _unpSuffix  = "";
    string _outFileName = "";
    int _bytesToSkip = 0;
    int _pixelsPerByte = 0;
    

    switch (options.header)
    {
        case "picture":
            Tuple<string, int> res = ParsePictureHeader(_inFileName);
            _unpSuffix = res.Item1;
            _pixelsPerByte = res.Item2;  
            _bytesToSkip = 16; //header length
            break;
        default:
            _unpSuffix = "-unpacked";
            break;
    }
    
    _outFileName = Path.GetDirectoryName(_inFileName) 
                + "\\" 
                + Path.GetFileNameWithoutExtension(_inFileName) 
                + _unpSuffix 
                + Path.GetExtension(_inFileName);    
    


    Stream inFileStream = new FileStream(_inFileName, FileMode.Open);
    List<Byte> resultBuffer = UnpackStream(new BinaryReader(inFileStream), _bytesToSkip);

    using(var outFileStream = File.Open(_outFileName, FileMode.Create))
    {
        using(BinaryWriter writer = new BinaryWriter(outFileStream))
        {
            foreach (byte symbol in resultBuffer)
            {
                switch (_pixelsPerByte)
                {
                    case 1: // VGA palette, 256-bit
                        writer.Write(symbol); 
                        break;
                    case 2: // EGA palette, 16-bit
                        //AB -> 0A 0B
                        byte byteA = (byte)((symbol & 0xF0) >> 4);
                        byte byteB = (byte)((symbol & 0x0F));

                        writer.Write(byteA); 
                        writer.Write(byteB); 
                        break;
                    case 4: //CGA palette, 4-bit
                        //AB -> 0Ah 0Al 0Bh 0Bl
                        byte byteAh = (byte)((symbol & 0xC0) >> 6);
                        byte byteAl = (byte)((symbol & 0x30) >> 4);
                        byte byteBh = (byte)((symbol & 0x0C) >> 2);
                        byte byteBl = (byte)((symbol & 0x03));

                        // //CGA convert
                        // writer.Write((byteAh == 0)?(byte)0:(byte)(10 + (byteAh * 2 -1)));
                        // writer.Write((byteAl == 0)?(byte)0:(byte)(10 + (byteAl * 2 -1)));
                        // writer.Write((byteBh == 0)?(byte)0:(byte)(10 + (byteBh * 2 -1)));
                        // writer.Write((byteBl == 0)?(byte)0:(byte)(10 + (byteBl * 2 -1))); 
                        writer.Write(byteAh);
                        writer.Write(byteAl);
                        writer.Write(byteBh); 
                        writer.Write(byteBl);  
                        break;
                    default:
                        writer.Write(symbol); 
                        break;
                }

            }
        }
    }

    inFileStream.Close();


    return;
}


Tuple<string, int> ParsePictureHeader(string _inFileName){
    string result ="";
    
    Stream inFileStream = new FileStream(_inFileName, FileMode.Open);
    BinaryReader reader = new BinaryReader(inFileStream);   
    int xSize = 0, ySize = 0, numOfColors = 0;
    //skip "PXPK"
    reader.ReadInt32();
    //read num of colors (if 16 - each byte describes 2 pixels!!)
    numOfColors = reader.ReadInt16();
    //read picture's x size
    xSize = reader.ReadInt16();
    //skip some strange
    reader.ReadInt16();
    //read picture's y size
    ySize = reader.ReadInt16();
    result = result + "-picture-" + xSize + "x" + ySize + "x" + numOfColors + "-unpacked";
    reader.Dispose();
    inFileStream.Close();



    return new Tuple<string, int>(result, (int)(8 / Math.Log2(numOfColors)));
}

List<byte> UnpackStream(BinaryReader reader, int skipCount = 0){

    //to skip pic header
    for (int i = 0; i < skipCount; i++)
    {
        reader.ReadByte();
    }

    /* get announced archive size */
    int resultBufferSize = reader.ReadUInt16();             

    /* skipping 2 zeroes - what they for?*/
    reader.ReadInt16();

    /*creating buffer */
    List<byte> resultBuffer = new List<byte>();



    
    for (int i = 0; i < PRE_BUFFER_LENGTH; i++)
    {
        resultBuffer.Add(0);
    }


    

    /*
    1. Get flags
    2. for each flag either
    2.1. write content or
    2.2. treat loop
    */
    bool streamNotEnded = true;
    int chunkNum = 0;

    /* For each chunk (and chunk can end abruptly, so we need to handle it by monitoring stream end) */
    while (streamNotEnded)
    {
        /* 1. Get flags for this chunk */
        byte flags = 0;
        try{
            flags = reader.ReadByte();
            chunkNum++;
        }
        catch(EndOfStreamException e)
        {
            Console.WriteLine("No more chunks left. Out.");
            streamNotEnded = false;
            break;
        }
#if DEBUG
/* Diagnostic */ Console.Write((chunkNum-1) + "(f-");
/* Diagnostic */ Console.Write(flags.ToString() + ", rb-");
/* Diagnostic */ Console.Write(resultBuffer.Count() + "):");
#endif        
        /* 2. For each entry in the chunk (8 max, cause there are 8 bits in byte, which represents flags*/
        for (byte index = 0; index < 8; index++)
        {
            /* 2.1. Update length of unpacked content (omitting pre-window for the sake of clarity) */
            UInt16 unpackedLength = (UInt16)resultBuffer.Count();//(UInt16)(resultBuffer.Count() - PREWINDOW_LENGTH);
            /* 2.1.1. Get 3 lowest bits of unpacked length = position in curent window of 0xFFF length */
            UInt16 currPosInWindow = (UInt16)(unpackedLength & 0x0FFF); 
            /* 2.1.2. Get the highest bit of unpacked length = number of current window of 0xFFF length */
            UInt16 currWindowNumber = (UInt16)(unpackedLength & 0xF000);  

            /* 2.2. Extract flag value from flags for current position in this chunk. Convert it into boolean for simplicity */
            bool isValue = Convert.ToBoolean((flags >> (index)) & 1);

            /* 2.2.1. If current chunk entry should be treated as a value (1-byte long) */
            if(isValue)
            {
                byte value;
                //get value
                try
                {
                    value = reader.ReadByte();
                }
                catch(EndOfStreamException e)
                {
                    Console.WriteLine("Flag announced value but stream has ended. Out.");
                    return null;
                }
#if DEBUG                
/* Diagnostic */Console.Write(Convert.ToChar(value));
#endif
                //add it to unpacked content
                resultBuffer.Add(value);
            }
            
            /* 2.2.2 If current chunk entry should be treated as a link (2-byte long) */
            else
            {
                //get values
                byte byteAB;
                byte byteCD; 
                try
                {
                    byteAB = reader.ReadByte();
                    byteCD  = reader.ReadByte();
                }
                catch(EndOfStreamException e)
                {
                    Console.WriteLine("Flag announced link but stream has ended. Flushing contents.");
                    streamNotEnded = false;
                    break;
                }

                /* Calculating offset and length of repeated part
                   Structure: AB CD -> CAB = link offset (in FFF window, prewindow correction needed), D = length 
                   2.2.2.1. Length = D + default length
                   2.2.2.2. Offset = CAB + w# (multiplyer of 0x1000, cause window length is 0xFFF, 
                                               cause CAB is 3-byte long, so FFF is the max addressed value) 
                            w#: if (CAB < curPosInWindow-3) => w# = currWindowNumber else w# = currWindowNumber-1 
                            ASSUMPTION 1: link cannot refer to position further (bigger address) than current, cause it is not exist, 
                                          so it should be referring in position in the previous window (so W#-1)
                            ASSUMPTION 2: distance between same position in the current and previous window == 0x1000, and we cannot
                                          address more than 0x1000 with 3 bytes, so if links refers to postion that is previous to 
                                          current (smaller in number) - this position should be located in the current window (so #W)
                            ASSUMPTION 3: we take min possible closest repetition in account, thus pos-3
                                               */
                
                // 2.2.2.1. 
                // extract lower byte D
                int byteD = byteCD & ((1 << 4) - 1);
                // calculating final repeat length
                int repeatLength = byteD + MIN_REPEAT_LENGTH;
                
                // 2.2.2.2.
                // extract higher byte C
                UInt16 byteC = (UInt16)((byteCD >> 4) << 8);
                // combine C and AB, then adding slide window length.
                UInt16 byteCAB = (UInt16)(byteC + byteAB); // composing CAB
                //composing offset
                int offset = byteCAB + currWindowNumber;
                //adjusting offset 
                if (byteCAB + MAGIC_CONST < currPosInWindow )
                {
                    //same window case - should only take prewindow shift in account
                    offset += MAGIC_CONST;
                }
                else
                {
                    //previous window case - should split in just previous window or prewindow case
                    if (currWindowNumber == 0)
                    {
                        // prewindow case. no prewindow shift needed, obviously
                        // prewindow is just an 0x12 bytes-long end of "-1" window
                        // get 2 last bytes of address and reverse it - 
                        offset = MAGIC_CONST - (0x1000 - offset); //
                    }
                    else
                    {
                        //previous window case - reduce window number by 1 and add prewindow shift
                        offset -= 0x1000;
                        offset += MAGIC_CONST;
                    }

                }
                //got offset and length
#if DEBUG
/* Diagnostic */Console.Write("[" + offset.ToString() + ":");
#endif
                for (int i = 0; i < repeatLength; i++)
                {
                    var value = resultBuffer[offset + i];
                    resultBuffer.Add(value);
#if DEBUG
/* Diagnostic */    Console.Write(Convert.ToChar(value));
#endif
                }
#if DEBUG
/* Diagnostic */Console.Write("]");
#endif
            }
        }
#if DEBUG
/* Diagnostic */Console.WriteLine("----");
#endif
    }
    Console.WriteLine("Announced unpacked size in bytes: " + resultBufferSize);
    Console.WriteLine("Actual unpacked size in bytes   : " + (resultBuffer.Count()-PRE_BUFFER_LENGTH));
    
    resultBuffer.RemoveRange(0, PRE_BUFFER_LENGTH);
    return(resultBuffer);
}















