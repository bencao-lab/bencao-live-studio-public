using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

partial class Studio {
    CancellationTokenSource songStop;
    async void StreamSong(string path,LiveView session){
        playingSong=true;song.Text=Path.GetFileName(path);nextBeat=DateTime.MinValue;
        var stop=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);songStop=stop;
        try{
            using(var process=new Process()){
                process.StartInfo=new ProcessStartInfo("ffmpeg.exe","-nostdin -v error -i \""+path+"\" -vn -ar 24000 -ac 1 -f s16le pipe:1"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};
                process.Start();
                using(stop.Token.Register(delegate{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}})){
                    var stream=process.StandardOutput.BaseStream;
                    byte[] block=new byte[48000];bool first=true;
                    while(true){
                        int used=0;
                        while(used<block.Length){int n=await stream.ReadAsync(block,used,block.Length-used,stop.Token);if(n==0)break;used+=n;}
                        stop.Token.ThrowIfCancellationRequested();
                        if(used==0)break;
                        if(liveView!=session)break;
                        if(first){
                            first=false;session.Transition();
                            var intro=await Interaction.Speech("换个镜头，这首送给你。",stop.Token);
                            stop.Token.ThrowIfCancellationRequested();await session.Audio(intro);
                        }
                        long level=0;for(int i=0;i+1<used;i+=8)level+=Math.Abs((int)BitConverter.ToInt16(block,i));
                        if(level>used*50L)Beat();
                        byte[] pcm=block;if(used<block.Length){pcm=new byte[used];Array.Copy(block,pcm,used);}
                        await session.Audio(pcm);
                    }
                    if(first)throw new IOException("歌曲没有可播放的声音");
                }
            }
        }catch(OperationCanceledException){}
        catch(Exception ex){if(!stop.IsCancellationRequested&&!IsDisposed)replyStatus.Text="歌曲中断："+ex.Message;}
        finally{if(songStop==stop)songStop=null;stop.Dispose();if(!IsDisposed)FinishSong();}
    }
}
