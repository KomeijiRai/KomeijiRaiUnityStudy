using System;
using System.Threading.Tasks;
using Common;
using DotNetty.Buffers;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Orleans;

namespace GateServer.Net
{
    public class TcpServer
    {
        private IEventLoopGroup bossGroup;

        private IEventLoopGroup workerGroup;

        private IChannel bootstrapChannel;

        private readonly IClusterClient client;

        public TcpServer(IClusterClient client)
        {
            this.client = client;
        }

        /// <summary>
        /// 启动 TcpServer
        /// </summary>
        /// <returns></returns>
        public async Task StartAsync()
        {
            // 主工作组

            bossGroup = new MultithreadEventLoopGroup(1);

            // 子工作组 默认是 CPU核数 * 2

            workerGroup = new MultithreadEventLoopGroup();

            try
            {
                ServerBootstrap bootstrap = new ServerBootstrap();

                // 设置线程组模型为：主从线程模型

                bootstrap.Group(bossGroup, workerGroup);

                // 设置通道类型

                bootstrap.Channel<TcpServerSocketChannel>();

                bootstrap
                    // 半连接队列的元素上限 也就是说已经在操作系统层面完成了3次握手，等待当前进程取走的链路个数
                    .Option(ChannelOption.SoBacklog, 4096)
                    // 用于设置 Channel 接收字节流时的缓冲区大小
                    .Option(ChannelOption.RcvbufAllocator, new AdaptiveRecvByteBufAllocator())
                    // 用于设置重用缓冲区
                    .Option(ChannelOption.Allocator, PooledByteBufferAllocator.Default)
                    .ChildOption(ChannelOption.Allocator, PooledByteBufferAllocator.Default)
                    // 保持长连接
                    .ChildOption(ChannelOption.SoKeepalive, true)
                    // 取消延迟发送 也就是关闭 Nagie 算法
                    .ChildOption(ChannelOption.TcpNodelay, true)
                    // 用于对单个通道的数据处理
                    .ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
                    {
                        IChannelPipeline pipeline = channel.Pipeline;

                        pipeline.AddLast("IdleChecker", new IdleStateHandler(50, 50, 0));

                        pipeline.AddLast(new TcpServerEncoder(), new TcpServerDecoder(), new TcpServerHandler(client));
                    }));

                bootstrapChannel = await bootstrap.BindAsync(8899);

                Logger.Instance.Information($"启动网关服务器成功！监听端口号：8899");
            }
            catch (Exception e)
            {
                Logger.Instance.Error(e.Message);

                throw new Exception("启动 TcpServer 失败！\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 关闭 TcpServer
        /// </summary>
        /// <returns></returns>
        public async Task Stop()
        {
            await Task.WhenAll(
                bootstrapChannel.CloseAsync(),
                bossGroup.ShutdownGracefullyAsync(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
                workerGroup.ShutdownGracefullyAsync(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))
            );

            Logger.Instance.Information("关闭网关服务器成功！");
        }

    }
}