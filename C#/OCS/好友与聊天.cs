using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net.Sockets;
using System.Windows.Forms;
using PMS;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MySql.Data.MySqlClient;
using OCS.Resources;
using Org.BouncyCastle.Crypto.Tls;
using System.Diagnostics;

namespace OCS
{
    public partial class 好友与聊天 : Form
    {
        private MySqlConnection mySqlConnection = SetConnection.mySqlConnection;
        private User user;
        Socket clientSocket;
        static Boolean isListen = true;
        Thread thDataFromServer;
        IPAddress ipadr;
        private TreeNode _treeNode;
        public static List<string> group;

        public 好友与聊天(User user)
        {
            InitializeComponent();
            this.user = user;
            group = new List<string>();
            textBox3.Text = IPAddress.Loopback.ToString();
        }

        private void 好友与聊天_Load(object sender, EventArgs e)
        {
            treeView1.LabelEdit = true;
            DrawNode();
        }

        private void SendMessage()
        {
            if (String.IsNullOrWhiteSpace(textBox2.Text.Trim()))
            {
                MessageBox.Show("发送内容不能为空哦~");
                return;
            }

            if (clientSocket != null && clientSocket.Connected)
            {
                Byte[] bytesSend = Encoding.UTF8.GetBytes(textBox2.Text + "$");
                clientSocket.Send(bytesSend);
                textBox2.Text = "";
            }
            else
            {
                MessageBox.Show("未连接服务器或者服务器已停止，请联系管理员~");
            }
        }

        private void DrawNode()
        {
            treeView1.Nodes.Clear();
            group.Clear();
            try
            {
                string cmd = "SELECT distinct userGroup  from relationshipPlus where userId=@userId;"
                             + "SELECT friendId AS friends,userGroup as myGroup,userName as userName,friendName as friendName from relationshipPlus  WHERE userId =@userId  union ALL SELECT userId as friends, friendGroup as myGroup,friendName as userName,userName as friendName  from relationshipPlus  WHERE friendId = @userId";

                MySqlCommand mySqlCommand = new MySqlCommand(cmd, mySqlConnection);
                mySqlCommand.Parameters.Add("@userId", MySqlDbType.Int16);
                mySqlCommand.Parameters["@userId"].Value = user.UserId;

                MySqlDataAdapter mySqlDataAdapter = new MySqlDataAdapter();
                mySqlDataAdapter.SelectCommand = mySqlCommand;

                DataSet dataSet = new DataSet();
                mySqlDataAdapter.Fill(dataSet, "relationshipPlus");
                foreach (DataRow dataRow in dataSet.Tables[0].Rows)
                {
                    TreeNode treeNode = new TreeNode();
                    treeNode.Text = dataRow["userGroup"].ToString();
                    treeNode.Name = dataRow["userGroup"].ToString();
                    group.Add(dataRow["userGroup"].ToString());
                    treeView1.Nodes.Add(treeNode);
                    foreach (DataRow row in dataSet.Tables[1].Rows)
                    {
                        if (row["myGroup"].ToString().Equals(treeNode.Text))
                        {
                            treeNode.Nodes.Add(new TreeNode(row["friendName"] + "(" + row["friends"] + ")"));
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
                throw;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            SendMessage();
        }

        private void setConnect()
        {
            if (String.IsNullOrWhiteSpace(user.UserName))
            {
                MessageBox.Show("请设置用户名哦亲");
            }

            if (clientSocket == null || !clientSocket.Connected)
            {
                try
                {
                    clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    //参考网址： https://msdn.microsoft.com/zh-cn/library/6aeby4wt.aspx
                    // Socket.BeginConnect 方法 (String, Int32, AsyncCallback, Object)
                    //开始一个对远程主机连接的异步请求
                    /* string host,     远程主机名
                     * int port,        远程主机的端口
                     * AsyncCallback requestCallback,   一个 AsyncCallback 委托，它引用连接操作完成时要调用的方法，也是一个异步的操作
                     * object state     一个用户定义对象，其中包含连接操作的相关信息。 当操作完成时，此对象会被传递给 requestCallback 委托
                     */
                    //如果txtIP里面有值，就选择填入的IP作为服务器IP，不填的话就默认是本机的
                    if (!String.IsNullOrWhiteSpace(textBox3.Text.ToString().Trim()))
                    {
                        try
                        {
                            ipadr = IPAddress.Parse(textBox3.Text.ToString().Trim());
                        }
                        catch
                        {
                            MessageBox.Show("请输入正确的IP后重试");
                            return;
                        }
                    }
                    else
                    {
                        ipadr = IPAddress.Loopback;
                    }
                    clientSocket.BeginConnect(ipadr, 8080, args =>
                    {
                        if (args.IsCompleted) //判断该异步操作是否执行完毕
                        {
                            Byte[] bytesSend = new Byte[4096];

                            bytesSend = Encoding.UTF8.GetBytes(user.UserId + "$");
                            if (clientSocket != null && clientSocket.Connected)
                            {
                                clientSocket.Send(bytesSend);
                                //textBox2.Focus(); //将焦点放在
                                thDataFromServer = new Thread(DataFromServer);
                                thDataFromServer.IsBackground = true;
                                thDataFromServer.Start();
                            }
                            else
                            {
                                MessageBox.Show("服务器已关闭");
                            }
                        }
                        textBox3.BeginInvoke(new Action(() =>
                        {
                            if (clientSocket != null && clientSocket.Connected)
                            {
                                textBox3.Enabled = false;
                            }
                        }));
                    }, null);
                }
                catch (SocketException ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
            else
            {
                MessageBox.Show("你已经连接上服务器了");
            }
        }


        private void DataFromServer()
        {
            ShowMsg("Connected to the Chat Server...");
            isListen = true;
            try
            {
                while (isListen)
                {
                    Byte[] bytesFrom = new Byte[4096];
                    Int32 len = clientSocket.Receive(bytesFrom);

                    String dataFromClient = Encoding.UTF8.GetString(bytesFrom, 0, len);
                    if (!String.IsNullOrWhiteSpace(dataFromClient))
                    {
                        //如果收到服务器已经关闭的消息，那么就把客户端接口关了，免得出错，并在客户端界面上显示出来
                        if (dataFromClient.Length >= 17 &&
                            dataFromClient.Substring(0, 17).Equals("Server has closed"))
                        {
                            clientSocket.Close();
                            clientSocket = null;

                            textBox1.BeginInvoke(new Action(() =>
                            {
                                textBox1.Text += Environment.NewLine + "服务器已关闭";
                            }));

                            thDataFromServer.Abort(); //这一句必须放在最后，不然这个进程都关了后面的就不会执行了

                            return;
                        }
                       //ShowMsg(dataFromClient);
                       //Debug.WriteLine(dataFromClient);
                           if (dataFromClient.StartsWith("#") && dataFromClient.EndsWith("#"))
                           {
                               String userName = dataFromClient.Substring(1, dataFromClient.Length - 2);
                               //MessageBox.Show("用户名：[" + userName + "]已经存在，请尝试其他用户名并重试");

                               isListen = false;
                               clientSocket.Send(Encoding.UTF8.GetBytes("$"));
                               clientSocket.Close();
                               clientSocket = null;
                           }
                           else
                           {
                               //txtName.Enabled = false;    //当用户名唯一时才禁止再次输入用户名
                               ShowMsg(dataFromClient);
                           }
                    }
                }
            }
            catch (SocketException ex)
            {
                isListen = false;
                if (clientSocket != null && clientSocket.Connected)
                {
                    //没有在客户端关闭连接，而是给服务器发送一个消息，在服务器端关闭连接
                    //这样可以将异常的处理放到服务器。客户端关闭会让客户端和服务器都抛异常
                    clientSocket.Send(Encoding.UTF8.GetBytes("$"));
                    MessageBox.Show(ex.ToString());
                }
            }
        }

        private void ShowMsg(String msg)
        {
            textBox1.BeginInvoke(new Action(() =>
            {
                textBox1.Text +=
                Environment.NewLine + msg; // 在 Windows 环境中，C# 语言 Environment.NewLine == "\r\n" 结果为 true
                                           //txtReceiveMsg.ScrollToCaret();

            }));
        }

        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right) //判断点击的是否为右键
            {
                if (e.Node.Level == 0) //如果是第一级节点处右键，显示菜单
                {
                    修改组名ToolStripMenuItem.Visible = true;
                    查看资料ToolStripMenuItem.Visible = false;
                    移动分组ToolStripMenuItem.Visible = false;
                    删除好友ToolStripMenuItem.Visible = false;
                }
                else if (e.Node.Level == 1) //如果是第二级节点右键，显示菜单
                {
                    修改组名ToolStripMenuItem.Visible = false;
                    查看资料ToolStripMenuItem.Visible = true;
                    移动分组ToolStripMenuItem.Visible = true;
                    删除好友ToolStripMenuItem.Visible = true;
                }

                _treeNode = e.Node;
            }
        }

        private void contextMenuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            switch (e.ClickedItem.Text)
            {
                case "查看资料":
                    checkData();
                    break;
                case "删除好友":
                    deleteData();
                    break;
                case "移动分组":
                    removeGroup();
                    DrawNode();
                    break;
                case "修改组名":
                    修改组名 form = new 修改组名(_treeNode.Text);
                    form.changeGroupName += changeGroup;
                    form.ShowDialog();
                    break;
                case "刷新":
                    DrawNode();
                    break;
            }
        }

        private void checkData()
        {
            string userId = Regex.Replace(_treeNode.Text, @"(.*\()(.*)(\).*)", "$2");
            查看资料 form = new 查看资料(userId);
            form.Show();
        }

        private void deleteData()
        {
            DialogResult dialog =
                MessageBox.Show("确认删除？", "删除好友", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
            if (dialog == DialogResult.Yes)
            {
                string friendId = Regex.Replace(_treeNode.Text, @"(.*\()(.*)(\).*)", "$2");
                string cmd =
                    "delete from relationship where (userId=@userId and friendId=@friendId) or (userId=@friendId and friendId=@userId)";
                MySqlCommand mySqlCommand = new MySqlCommand(cmd, mySqlConnection);
                mySqlCommand.Parameters.Add("@userId", MySqlDbType.Int16);
                mySqlCommand.Parameters["@userId"].Value = user.UserId;
                mySqlCommand.Parameters.Add("@friendId", MySqlDbType.Int16);
                mySqlCommand.Parameters["@friendId"].Value = friendId;
                if (mySqlCommand.ExecuteNonQuery() == 1)
                {
                    MessageBox.Show("删除成功！");
                    _treeNode.Remove();
                }
                else
                    MessageBox.Show("删除失败。", "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void removeGroup()
        {
            try
            {
                移动分组 form = new 移动分组(_treeNode.Text, _treeNode.Parent.Text);
                form.Show();
            }
            catch (Exception e)
            {
                MessageBox.Show("移动失败。", "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        public void MoveNode(string result)
        {
            _treeNode.Remove();
            try
            {
                treeView1.Nodes[result].Nodes.Add(_treeNode);
            }
            catch (Exception e)
            {
                TreeNode treeNode = new TreeNode();
                treeNode.Text = result;
                treeNode.Name = result;
                group.Add(result);
                treeView1.Nodes.Add(treeNode);
            }
            DrawNode();
        }

        public void changeGroup(string str)
        {
            string cmd =
                "update relationship set userGroup=@userGroupNew where userGroup=@userGroupOld;";
            MySqlCommand mySqlCommand = new MySqlCommand(cmd, mySqlConnection);
            mySqlCommand.Parameters.Add("@userGroupNew", MySqlDbType.String);
            mySqlCommand.Parameters["@userGroupNew"].Value = str;
            mySqlCommand.Parameters.Add("@userGroupOld", MySqlDbType.String);
            mySqlCommand.Parameters["@userGroupOld"].Value = _treeNode.Text;
            if (mySqlCommand.ExecuteNonQuery() != 0)
            {
                MessageBox.Show("修改成功！", "成功");
                _treeNode.Text = str;
            }
            else
                MessageBox.Show("修改失败!", "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            setConnect();
        }

        private void 好友与聊天_Shown(object sender, EventArgs e)
        {
            //setConnect();
        }

        private void 好友与聊天_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (clientSocket != null && clientSocket.Connected)
            {
                clientSocket.Send(Encoding.UTF8.GetBytes("$"));

                thDataFromServer.Abort();
                clientSocket.Send(Encoding.UTF8.GetBytes("$"));

                clientSocket.Close();
                clientSocket = null;
            }
        }
    }
}