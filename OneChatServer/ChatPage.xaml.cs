using Newtonsoft.Json;
using OneChatServer.Model;
using OneChatServer.Service;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// “空白页”项模板在 http://go.microsoft.com/fwlink/?LinkId=234238 上有介绍

namespace OneChatServer
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ChatPage : Page
    {
        public ChatPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;   //允许缓存页面
            ClientList = new ObservableCollection<ClientInfo>();
            MessageList = new ObservableCollection<MessageInfo>();
            OffButton.IsEnabled = false;
        }

        private StreamSocketListener listener = null;   //服务器监听Socket
        private ObservableCollection<ClientInfo> ClientList;   //在线用户信息列表
        private ObservableCollection<MessageInfo> MessageList;   //消息列表

        #region 开启监听
        /// <summary>
        /// 开启监听
        /// </summary>
        /// <param name="ServerPort">监听端口</param>
        private async void Listen(string ServerPort)
        {
            if (listener != null)
            {
                listener.Dispose();
                listener = null;
            }
            listener = new StreamSocketListener();

            try
            {
                ConnectionProgressRing.IsActive = true;
                OnButton.IsEnabled = false;

                await listener.BindServiceNameAsync(ServerPort);   //绑定监听端口
                listener.ConnectionReceived += listener_ConnectionReceived;   //注册监听完成事件
                DBManager.NotifyMsg(MessageList);   //输出聊天记录
                ConnectionProgressRing.IsActive = false;
                OnButton.IsEnabled = false;
                OffButton.IsEnabled = true;
            }
            catch
            {
                ConnectionProgressRing.IsActive = false;
                Disconnection();
            }
        }

        //连接成功时触发事件
        private void listener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            Received(args.Socket);   //开始异步接收信息
        }
        #endregion

        #region 接收信息
        /// <summary>
        /// 接收信息方法
        /// </summary>
        /// <param name="client">信息来源Socket</param>
        private async void Received(StreamSocket client)
        {
            DataReader reader = new DataReader(client.InputStream);   //创建Socket输入流读取器

            while (true)
            {
                //从输入流加载一个uint类型数据
                try   //判断连接是否断开
                {
                    uint size = await reader.LoadAsync(sizeof(uint));
                }
                catch
                {
                    foreach (var c in ClientList.ToList())
                    {
                        if (c.Socket == client)
                        {
                            ClientList.Remove(c);   //将指定用户从用户列表中移除
                        }
                    }

                    //将更新后的用户列表发送给所有在线用户
                    Task task = Task.Factory.StartNew(SendOnlineList);
                    task.Wait();
                    return;
                }
                uint length = reader.ReadUInt32();   //读取数据，该数据表示数据包长度
                await reader.LoadAsync(length);   //加载数据包
                byte[] buffer = new byte[length];   //用于存放接收的数据包
                reader.ReadBytes(buffer);   //加载数据包
                string jsonmessage = Encoding.UTF8.GetString(buffer);   //将二进制数组转换为字符串
                MessageInfo message = new MessageInfo();   //用于存放解析后的具体数据类
                message = JsonConvert.DeserializeObject<MessageInfo>(jsonmessage);   //将Json数据反序列化为MessageInfo类

                //更新UI线程元素
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    //判断信息类型
                    if (message.MsgType == MessageType.Update)    //首次连接
                    {
                        ClientList.Add(new ClientInfo { Name = message.Name, Socket = client });
                        Task task = Task.Factory.StartNew(SendOnlineList);
                        task.Wait();
                    }
                    else if (message.MsgType == MessageType.TextMessage)   //转发信息
                    {
                        DBManager.InsertMsg(message);   //向数据库插入信息
                        MessageList.Add(message);
                        StreamSocket socket = ClientList.First(c => c.Name == message.Target).Socket;
                        Task task = SendData(message, socket);   //向目标用户转发信息
                    }
                    else if (message.MsgType == MessageType.Disconnection)   //客户端下线
                    {
                        if (client != null)
                        {
                            client.Dispose();
                            client = null;
                        }
                        foreach (var c in ClientList.ToList())
                        {
                            if (c.Name == message.Name)
                            {
                                ClientList.Remove(c);   //将指定用户从用户列表中移除
                            }
                        }

                        //将当前用户列表发送给所有在线用户
                        Task task = Task.Factory.StartNew(SendOnlineList);
                        task.Wait();
                    }
                });
            }

        }
        #endregion

        #region 发送信息
        /// <summary>
        /// 发送信息方法
        /// </summary>
        /// <param name="data">要发送的对象</param>
        /// <param name="client">发送目标Socket</param>
        private async Task SendData(MessageInfo data, StreamSocket client)
        {
            string jsonmessage = JsonConvert.SerializeObject(data);   //将数据序列化为Json数据格式
            byte[] buffer = Encoding.UTF8.GetBytes(jsonmessage);   //将Json数据转换为二进制数组

            using (DataWriter writer = new DataWriter(client.OutputStream))   //创建目标Socket输出流编写器
            {
                uint length = (uint)buffer.Length;
                writer.WriteUInt32(length);   //写入数据包大小
                writer.WriteBytes(buffer);   //写入数据包
                await writer.StoreAsync();   //提交数据
                writer.DetachStream();   //分离与编写器关联的流
            }
        }
        #endregion

        private void SendOnlineList()   //广播在线用户列表
        {
            var onlineuser = ClientList.ToList().Select(c => c.Name).ToList();
            MessageInfo msg = new MessageInfo { MsgType = MessageType.Update, Name = "SERVER", OnlineUser = onlineuser };
            ClientList.ToList().ForEach(async c => { await SendData(msg, c.Socket); });
        }

        private void OnButton_Click(object sender, RoutedEventArgs e)
        {
            string ServerPort = ServerPortTextBox.Text.Trim();
            if (Regex.IsMatch(ServerPort, "\\d{1,5}") && Int32.Parse(ServerPort) < 65536)   //判断输入格式
            {
                Listen(ServerPort);   //异步尝试监听指定端口
            }
            else
            {
                ShowMessage("端口格式错误！");
            }
        }

        private void OffButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnection();
        }

        private async void ShowMessage(string txt)   //消息提示
        {
            //更新UI线程元素
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                MessageDialog message = new MessageDialog(txt, "提示");
                await message.ShowAsync();
            });
        }

        private async void Disconnection()   //断开连接处理
        {
            if (listener != null)
            {
                listener.Dispose();
                listener = null;
            }

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                //关闭服务器
                OffButton.IsEnabled = false;
                OnButton.IsEnabled = true;

                ClientList.ToList().ForEach(c => c.Socket.Dispose());   //释放资源

                ClientList.Clear();
                MessageList.Clear();
                //释放监听Socket，关闭服务器

            });
        }

        private void OnlineButton_Click(object sender, RoutedEventArgs e)
        {
            //用户列表展开与关闭
            MySplitView.IsPaneOpen = !MySplitView.IsPaneOpen;
        }

        #region 清空数据库
        private async void CleanDBButton_Click(object sender, RoutedEventArgs e)
        {
            MessageDialog msg = new MessageDialog("确定要清空聊天记录吗？", "提示");
            UICommand cmdOK = new UICommand("确定", new UICommandInvokedHandler(CommandAct), 1);
            UICommand cmdCancel = new UICommand("取消");
            msg.Commands.Add(cmdOK);
            msg.Commands.Add(cmdCancel);
            await msg.ShowAsync();
        }

        private void CommandAct(IUICommand command)
        {
            int cmdID = (int)command.Id;
            if (cmdID == 1)
            {
                DBManager.DeleteMsg();
                DBManager.NotifyMsg(MessageList);
            }
        }
        #endregion
    }
}
