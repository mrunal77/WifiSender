import socket
import threading
import os
import struct
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
import uuid


class FileTransferApp:
    def __init__(self, root):
        self.root = root
        self.root.title("WifiSender - File Transfer")
        self.root.geometry("700x550")
        self.root.configure(bg="#F8FAFC")

        self.mode = tk.StringVar(value="send")
        self.selected_files = []
        self.receive_folder = ""
        self.is_receiving = False
        self.is_sending = False
        self.server_socket = None
        self.local_ip = self.get_local_ip()

        self.setup_ui()

    def get_local_ip(self):
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.connect(("8.8.8.8", 80))
            ip = s.getsockname()[0]
            s.close()
            return ip
        except Exception:
            return "127.0.0.1"

    def setup_ui(self):
        header_frame = tk.Frame(self.root, bg="#1E293B", height=80)
        header_frame.pack(fill="x")
        header_frame.pack_propagate(False)

        tk.Label(
            header_frame,
            text="WifiSender",
            font=("Segoe UI", 24, "bold"),
            fg="white",
            bg="#1E293B"
        ).pack(pady=15)

        main_frame = tk.Frame(self.root, bg="#F8FAFC")
        main_frame.pack(fill="both", expand=True, padx=20, pady=20)

        mode_frame = tk.Frame(main_frame, bg="#F8FAFC")
        mode_frame.pack(fill="x", pady=(0, 20))

        self.btn_send = tk.Button(
            mode_frame,
            text="SEND",
            font=("Segoe UI", 12, "bold"),
            bg="#2563EB",
            fg="white",
            activebackground="#1D4ED8",
            activeforeground="white",
            bd=0,
            padx=30,
            pady=10,
            cursor="hand2",
            command=lambda: self.set_mode("send")
        )
        self.btn_send.pack(side="left", padx=(0, 10))

        self.btn_receive = tk.Button(
            mode_frame,
            text="RECEIVE",
            font=("Segoe UI", 12, "bold"),
            bg="#E2E8F0",
            fg="#64748B",
            activebackground="#CBD5E1",
            activeforeground="#1E293B",
            bd=0,
            padx=30,
            pady=10,
            cursor="hand2",
            command=lambda: self.set_mode("receive")
        )
        self.btn_receive.pack(side="left")

        self.connection_frame = tk.LabelFrame(
            main_frame,
            text="Connection",
            font=("Segoe UI", 11, "bold"),
            bg="#F8FAFC",
            fg="#1E293B",
            bd=2,
            relief="groove",
            padx=15,
            pady=15
        )
        self.connection_frame.pack(fill="x", pady=(0, 20))

        ip_label = tk.Label(
            self.connection_frame,
            text="Your IP Address:",
            font=("Segoe UI", 10),
            bg="#F8FAFC",
            fg="#64748B"
        )
        ip_label.grid(row=0, column=0, sticky="w", pady=5)

        self.ip_display = tk.Label(
            self.connection_frame,
            text=f"{self.local_ip}",
            font=("Segoe UI", 12, "bold"),
            bg="#F8FAFC",
            fg="#10B981"
        )
        self.ip_display.grid(row=0, column=1, sticky="w", padx=(10, 0), pady=5)

        port_label = tk.Label(
            self.connection_frame,
            text="Port:",
            font=("Segoe UI", 10),
            bg="#F8FAFC",
            fg="#64748B"
        )
        port_label.grid(row=1, column=0, sticky="w", pady=5)

        self.port_entry = tk.Entry(
            self.connection_frame,
            font=("Segoe UI", 11),
            width=10,
            bg="white",
            relief="solid",
            bd=1
        )
        self.port_entry.insert(0, "5555")
        self.port_entry.grid(row=1, column=1, sticky="w", padx=(10, 0), pady=5)

        self.recipient_label = tk.Label(
            self.connection_frame,
            text="Recipient IP:",
            font=("Segoe UI", 10),
            bg="#F8FAFC",
            fg="#64748B"
        )
        self.recipient_label.grid(row=2, column=0, sticky="w", pady=5)

        self.recipient_entry = tk.Entry(
            self.connection_frame,
            font=("Segoe UI", 11),
            width=18,
            bg="white",
            relief="solid",
            bd=1
        )
        self.recipient_entry.grid(row=2, column=1, sticky="w", padx=(10, 0), pady=5)

        self.file_frame = tk.LabelFrame(
            main_frame,
            text="Files",
            font=("Segoe UI", 11, "bold"),
            bg="#F8FAFC",
            fg="#1E293B",
            bd=2,
            relief="groove",
            padx=15,
            pady=15
        )
        self.file_frame.pack(fill="both", expand=True, pady=(0, 20))

        self.select_file_btn = tk.Button(
            self.file_frame,
            text="Select Files",
            font=("Segoe UI", 10),
            bg="#2563EB",
            fg="white",
            activebackground="#1D4ED8",
            bd=0,
            padx=20,
            pady=8,
            cursor="hand2",
            command=self.select_files
        )
        self.select_file_btn.pack(anchor="w", pady=(0, 10))

        self.files_listbox = tk.Listbox(
            self.file_frame,
            font=("Segoe UI", 10),
            bg="white",
            height=6,
            bd=1,
            relief="solid"
        )
        self.files_listbox.pack(fill="both", expand=True)

        self.download_frame = tk.Frame(self.file_frame, bg="#F8FAFC")
        self.download_frame.pack(fill="x", pady=(10, 0))

        self.select_folder_btn = tk.Button(
            self.download_frame,
            text="Select Download Folder",
            font=("Segoe UI", 10),
            bg="#2563EB",
            fg="white",
            activebackground="#1D4ED8",
            bd=0,
            padx=20,
            pady=8,
            cursor="hand2",
            command=self.select_download_folder
        )
        self.select_folder_btn.pack(side="left")

        self.folder_label = tk.Label(
            self.download_frame,
            text="No folder selected",
            font=("Segoe UI", 9),
            bg="#F8FAFC",
            fg="#64748B"
        )
        self.folder_label.pack(side="left", padx=(10, 0))

        self.action_frame = tk.Frame(main_frame, bg="#F8FAFC")
        self.action_frame.pack(fill="x")

        self.action_btn = tk.Button(
            self.action_frame,
            text="SEND FILES",
            font=("Segoe UI", 12, "bold"),
            bg="#10B981",
            fg="white",
            activebackground="#059669",
            bd=0,
            padx=40,
            pady=12,
            cursor="hand2",
            command=self.action_clicked
        )
        self.action_btn.pack()

        self.progress_frame = tk.Frame(main_frame, bg="#F8FAFC")
        self.progress_frame.pack(fill="x", pady=(15, 0))

        self.progress_label = tk.Label(
            self.progress_frame,
            text="",
            font=("Segoe UI", 10),
            bg="#F8FAFC",
            fg="#64748B"
        )
        self.progress_label.pack(anchor="w")

        self.progress_bar = ttk.Progressbar(
            self.progress_frame,
            mode="determinate",
            length=660
        )
        self.progress_bar.pack(fill="x", pady=(5, 0))

        self.status_label = tk.Label(
            main_frame,
            text="Ready",
            font=("Segoe UI", 10),
            bg="#F8FAFC",
            fg="#64748B"
        )
        self.status_label.pack(anchor="w", pady=(10, 0))

        self.set_mode("send")

    def set_mode(self, mode):
        self.mode.set(mode)
        if mode == "send":
            self.btn_send.configure(bg="#2563EB", fg="white", activebackground="#1D4ED8", activeforeground="white")
            self.btn_receive.configure(bg="#E2E8F0", fg="#64748B", activebackground="#CBD5E1", activeforeground="#1E293B")
            self.action_btn.configure(text="SEND FILES", bg="#10B981", activebackground="#059669", command=self.action_clicked)
            self.recipient_label.grid()
            self.recipient_entry.grid()
            self.select_file_btn.pack(anchor="w", pady=(0, 10))
            self.files_listbox.pack(fill="both", expand=True)
            self.select_folder_btn.pack_forget()
            self.folder_label.pack_forget()
        else:
            self.btn_receive.configure(bg="#2563EB", fg="white", activebackground="#1D4ED8", activeforeground="white")
            self.btn_send.configure(bg="#E2E8F0", fg="#64748B", activebackground="#CBD5E1", activeforeground="#1E293B")
            self.action_btn.configure(text="START RECEIVING", bg="#10B981", activebackground="#059669", command=self.action_clicked)
            self.recipient_label.grid_remove()
            self.recipient_entry.grid_remove()
            self.select_file_btn.pack_forget()
            self.files_listbox.pack_forget()
            self.select_folder_btn.pack(side="left")
            self.folder_label.pack(side="left", padx=(10, 0))

    def select_files(self):
        files = filedialog.askopenfiles(title="Select files to send")
        if files:
            self.selected_files = [f.name for f in files]
            self.files_listbox.delete(0, tk.END)
            for f in self.selected_files:
                self.files_listbox.insert(tk.END, os.path.basename(f))

    def select_download_folder(self):
        folder = filedialog.askdirectory(title="Select download folder")
        if folder:
            self.receive_folder = folder
            self.folder_label.configure(text=folder)

    def action_clicked(self):
        if self.mode.get() == "send":
            self.start_send()
        else:
            self.start_receive()

    def start_send(self):
        if not self.selected_files:
            messagebox.showwarning("No Files", "Please select files to send")
            return

        recipient_ip = self.recipient_entry.get().strip()
        if not recipient_ip:
            messagebox.showwarning("Missing IP", "Please enter recipient IP address")
            return

        try:
            port = int(self.port_entry.get().strip())
        except ValueError:
            messagebox.showwarning("Invalid Port", "Please enter a valid port number")
            return

        self.is_sending = True
        self.action_btn.configure(state="disabled", text="SENDING...")
        threading.Thread(target=self.send_files_thread, args=(recipient_ip, port), daemon=True).start()

    def send_files_thread(self, recipient_ip, port):
        try:
            total_files = len(self.selected_files)
            self.update_status(f"Connecting to {recipient_ip}:{port}...")

            client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            client_socket.settimeout(30)
            client_socket.connect((recipient_ip, port))

            self.update_status("Connected! Sending files...")

            for idx, file_path in enumerate(self.selected_files):
                if not self.is_sending:
                    break

                filename = os.path.basename(file_path)
                file_size = os.path.getsize(file_path)

                client_socket.sendall(struct.pack("utf-8", filename))
                client_socket.sendall(struct.pack(">Q", file_size))

                sent = 0
                with open(file_path, "rb") as f:
                    while sent < file_size:
                        chunk = f.read(65536)
                        if not chunk:
                            break
                        client_socket.sendall(chunk)
                        sent += len(chunk)
                        progress = ((idx * 100) + (sent * 100 / file_size)) / total_files
                        self.update_progress(progress)

                self.update_status(f"Sent {idx + 1}/{total_files}: {filename}")

            client_socket.sendall(b"__END__")

            client_socket.close()
            self.update_status("All files sent successfully!")
            self.update_progress(100)

        except socket.timeout:
            self.update_status("Connection timeout")
            messagebox.showerror("Error", "Connection timed out")
        except ConnectionRefusedError:
            self.update_status("Connection refused")
            messagebox.showerror("Error", "Connection refused. Make sure receiver is running.")
        except Exception as e:
            self.update_status(f"Error: {str(e)}")
            messagebox.showerror("Error", str(e))
        finally:
            self.root.after(0, lambda: self.action_btn.configure(state="normal", text="SEND FILES"))
            self.is_sending = False

    def start_receive(self):
        if not self.receive_folder:
            self.receive_folder = os.path.expanduser("~/Downloads")
            self.folder_label.configure(text=self.receive_folder)

        try:
            port = int(self.port_entry.get().strip())
        except ValueError:
            messagebox.showwarning("Invalid Port", "Please enter a valid port number")
            return

        self.is_receiving = True
        self.action_btn.configure(state="disabled", text="LISTENING...")
        threading.Thread(target=self.receive_files_thread, args=(port,), daemon=True).start()

    def receive_files_thread(self, port):
        try:
            self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.server_socket.bind(("0.0.0.0", port))
            self.server_socket.listen(1)
            self.server_socket.settimeout(60)

            self.update_status(f"Listening on port {port}...")

            while self.is_receiving:
                try:
                    client_socket, address = self.server_socket.accept()
                    self.update_status(f"Connection from {address[0]}")

                    while True:
                        filename_bytes = client_socket.recv(1024)
                        if filename_bytes == b"__END__":
                            break
                        if not filename_bytes:
                            break

                        filename = filename_bytes.decode("utf-8")

                        size_data = client_socket.recv(8)
                        file_size = struct.unpack(">Q", size_data)[0]

                        save_path = os.path.join(self.receive_folder, filename)
                        received = 0

                        with open(save_path, "wb") as f:
                            while received < file_size:
                                chunk = client_socket.recv(65536)
                                if not chunk:
                                    break
                                f.write(chunk)
                                received += len(chunk)
                                progress = (received * 100) / file_size
                                self.update_progress(progress)

                        self.update_status(f"Received: {filename}")

                    client_socket.close()
                    self.update_status("Transfer complete!")
                    self.update_progress(100)
                    break

                except socket.timeout:
                    if self.is_receiving:
                        self.update_status("Waiting for connection...")
                    continue

        except OSError as e:
            if e.errno == 98:
                self.update_status("Port already in use")
                messagebox.showerror("Error", "Port is already in use. Try a different port.")
            else:
                self.update_status(f"Error: {str(e)}")
                messagebox.showerror("Error", str(e))
        except Exception as e:
            self.update_status(f"Error: {str(e)}")
            messagebox.showerror("Error", str(e))
        finally:
            self.root.after(0, lambda: self.action_btn.configure(state="normal", text="START RECEIVING"))
            self.is_receiving = False

    def update_status(self, text):
        self.root.after(0, lambda: self.status_label.configure(text=text))

    def update_progress(self, value):
        self.root.after(0, lambda: self.progress_bar.configure(value=value))

    def on_closing(self):
        self.is_sending = False
        self.is_receiving = False
        if self.server_socket:
            self.server_socket.close()
        self.root.destroy()


def main():
    root = tk.Tk()
    app = FileTransferApp(root)
    root.protocol("WM_DELETE_WINDOW", app.on_closing)
    root.mainloop()


if __name__ == "__main__":
    main()
