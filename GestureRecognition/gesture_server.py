#!/usr/bin/env python3

import os
import glob
import time
import tensorflow as tf
import cv2
import matplotlib
import numpy as np
import matplotlib.pyplot as plt
import copy
from picamera.array import PiRGBArray
from picamera import PiCamera
from threading import Thread
from imutils.video.pivideostream import PiVideoStream

from bluetooth import *

os.environ['TF_CPP_MIN_LOG_LEVEL']='2'

class PiVideoStream:
	def __init__(self, resolution=(640, 480), framerate=60):
		# initialize the camera and stream
		self.camera = PiCamera()
		self.camera.resolution = resolution
		self.camera.framerate = framerate
		self.rawCapture = PiRGBArray(self.camera, size=resolution)
		self.stream = self.camera.capture_continuous(self.rawCapture,
			format="bgr", use_video_port=True)

		# initialize the frame and the variable used to indicate
		# if the thread should be stopped
		self.frame = None
		self.stopped = False
		
	def start(self):
		# start the thread to read frames from the video stream
		Thread(target=self.update, args=()).start()
		return self

	def update(self):
		# keep looping infinitely until the thread is stopped
		for f in self.stream:
			# grab the frame from the stream and clear the stream in
			# preparation for the next frame
			self.frame = f.array
			self.rawCapture.truncate(0)

			# if the thread indicator variable is set, stop the thread
			# and resource camera resources
			if self.stopped:
				self.stream.close()
				self.rawCapture.close()
				self.camera.close()
				return
				
	def read(self):
		# return the frame most recently read
		return self.frame
 
	def stop(self):
		# indicate that the thread should be stopped
		self.stopped = True

# Initializes Bluetooth connection with app
def InitializeConnection():
	# Set up bluetooth socket
    server_sock = BluetoothSocket( RFCOMM )
    server_sock.bind(("",PORT_ANY))
    server_sock.listen(1)

    port = server_sock.getsockname()[1]
	
	# Set service uuid
    uuid = "94f39d29-7d6d-437d-973b-fba39e49d4ee"

	# Create service
    advertise_service( server_sock, "GestureServer",
                       service_id = uuid,
                       service_classes = [ uuid, SERIAL_PORT_CLASS ],
                       profiles = [ SERIAL_PORT_PROFILE ]
                     )
	
	# Open socket and initialize handshake
    print ("Waiting for connection on channel %d" % port)
    client_sock, client_info = server_sock.accept()
    print ("Accepted connection from ", client_info)
    client_sock.send("hi")

	# Wait for answer from app
    while True:
        req = client_sock.recv(1024)
        if req.decode("utf-8") in ("hello\n"):
            print("Received handshake from ", client_info)
            break
			
    return client_sock, server_sock

# TF prediction method
def predict(image_data):
    predictions = sess.run(softmax_tensor, \
            {'DecodeJpeg/contents:0': image_data})
			
    # Sort to show labels in order of confidence
    top_k = predictions[0].argsort()[-len(predictions[0]):][::-1]

    max_score = 0.0
    res = ''
	
	# Sort letter with biggest score
    for node_id in top_k:
        human_string = label_lines[node_id]
        score = predictions[0][node_id]
        if score > max_score:
            max_score = score
            res = human_string
			
    return res, max_score

#Main method
def FireItOff():
    print("Starting...")
	
    # Loads label file
    label_lines = [line.rstrip() for line
                        in tf.gfile.GFile("logs_old/output_labels.txt")]
						
    #Parse graph from file
    with tf.gfile.FastGFile("logs_old/output_graph.pb", 'rb') as f:
        graph_def = tf.GraphDef()
        graph_def.ParseFromString(f.read())
        _ = tf.import_graph_def(graph_def, name='')

    try:
        with tf.Session() as sess:
            # Feed data
            softmax_tensor = sess.graph.get_tensor_by_name('final_result:0')
			
            # Set up bluetooth sockets
			try:
				client_sock, server_sock = Initialize()
			except Exception as e:
				print("Problems with connection setup: ", str(e))
				return
            
            c, i = 0, 0
            score = 0.0
            res = ''
            x1, y1, x2, y2 = 100, 100, 300, 300
            
			# Camera stream setup
            vs = PiVideoStream().start()
            time.sleep(2.0)
            
            if vs is None:
                print("Could not get camera device")
                return
				
			print("Initialized main components")
			
			# Loop until exit command or interrupt recieved
            while True:
                try:
				   # Read command from socket
                   request = client_sock.recv(1024)
				
                   if len(request) == 0:
                       print("Bad request from client")
                       pass
				   else:
					   # If exit recieved, exit server
                       command = request.decode("utf-8")
					   print("Received: ", command)
					   if command in ("exit\n"):
							print("Server closed by user request")
							return 
					
					# Capture camera data and mirror flip it
                    img = vs.read()
                    img = cv2.flip(img, 1)
                    
                    if img is not None:
						# Crop image to described dimensions and convert to data
                        img_cropped = img[y1:y2, x1:x2]
                        c += 1
                        image_data = cv2.imencode('.jpg', img_cropped)[1].tostring()
                        a = cv2.waitKey(33)
						# Predict second frame for more stability
                        if i == 2:
                            print("Predicting...")
							
							#Predict image data
                            res, score = predict(image_data)          
							
                            i = 0
                            print("Sending prediction data: ", res)
							
							# Send peredicted letter to the app
                            client_sock.send(res)
                        i += 1
						#Output current camera frame and show predicted value
                        cv2.putText(img, '%s' % (res.upper()), (100,400), cv2.FONT_HERSHEY_SIMPLEX, 4, (255,255,255), 4)
                        cv2.putText(img, '(score = %.5f)' % (float(score)), (100,450), cv2.FONT_HERSHEY_SIMPLEX, 1, (255,255,255))
                        mem = res
                        cv2.rectangle(img, (x1, y1), (x2, y2), (255,0,0), 2)
                        cv2.imshow("img", img)
                    else:
                        print("Error while capturing visual data")
                        pass
                except IOError:
                    print("Error while reading/writing data")
                    pass
                except KeyboardInterrupt:
                    print("Cleaning up and exiting...")
                    client_sock.close()
                    server_sock.close()
                    return
    except Exception as e:
        print("Runtime error: ", str(e))
        return
        
if __name__ == '__main__':
    FireItOff()

