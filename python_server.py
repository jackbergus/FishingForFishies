# -*- coding:utf-8 -*-

import socket
from datetime import datetime

def recvall(sock):
    BUFF_SIZE = 4096 # 4 KiB
    data = b''
    while True:
        part = sock.recv(BUFF_SIZE)
        data += part
        if len(part) < BUFF_SIZE:
            # either 0 or end of data
            break
    return data

def relational_learning_model(image)
    # returns the actions to be performed by the rover
    return "U"

# address and port is arbitrary
def server(host='127.0.0.1', port=60260):
  # create socket
  with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    sock.bind((host, port))
    print("[+] Listening on {0}:{1}".format(host, port))
    sock.listen(5)
    # permit to access
    conn, addr = sock.accept()

    with conn as c:
      # display the current time
      time = datetime.now().ctime()
      total_data = []
      print("[+] Connecting by {0}:{1} ({2})".format(addr[0], addr[1], time))

      while True:
        binary_image = recvall(conn)

        if not binary_image:
          print("[-] Not Received")
          break

        # the image is completely received
        print("[+] Received", len(binary_image))
        # TODO: do something with the image

        actions = relational_learning_model(binary_image)
        
        c.sendall(actions.encode('utf-8'))
        print("[+] Sending to {0}:{1}".format(addr[0], addr[1]))

if __name__ == "__main__":
  server()
